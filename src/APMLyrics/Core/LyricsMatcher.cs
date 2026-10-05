using System.Globalization;
using System.Text;

namespace APMLyrics.Core;

/// <summary>String rules that make SMTC names and catalog names comparable.</summary>
public static class TrackIdentity
{
    /// <summary>Apple packs the album into the artist field: "Artist - Album[ - Single]".</summary>
    public static string SplitArtist(string smtcArtist)
    {
        if (string.IsNullOrEmpty(smtcArtist))
            return string.Empty;

        // Apple does not use one dash. A real session reported
        // "Daniel Caesar \u2014 Son Of Spergy" with U+2014, and the first version
        // only looked for a hyphen, so the artist never split and every song
        // with an album suffix failed to match its lyrics. Accept the dash
        // family, each only when surrounded by spaces: "Jay-Z" is a name, not
        // an artist/album join.
        var dash = FindSpacedDash(smtcArtist);
        return dash < 0 ? smtcArtist.Trim() : smtcArtist[..dash].Trim();
    }

    /// <summary>Index of the first space-surrounded dash, or -1. Hyphen, en dash, em dash.</summary>
    private static int FindSpacedDash(string s)
    {
        for (var i = 1; i < s.Length - 1; i++)
        {
            var c = s[i];
            var isDash = c == '-' || c == '\u2013' || c == '\u2014';
            if (isDash && s[i - 1] == ' ' && s[i + 1] == ' ')
                return i;
        }
        return -1;
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        var lowered = s.Trim().ToLowerInvariant();
        foreach (var suffix in new[] { " - single", " - ep", " - live", " (single)", " (ep)" })
        {
            if (lowered.EndsWith(suffix, StringComparison.Ordinal))
                lowered = lowered[..^suffix.Length].Trim();
        }

        var stripped = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(stripped.Length);
        var lastWasSpace = false;
        foreach (var ch in stripped)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // accent-insensitive compare
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}

/// <summary>
/// Decides which cached lyric file belongs to the playing track.
/// Title and artist are the key; duration only breaks ties, because two songs
/// on one album can sit under half a second apart (spec section 2.5).
/// </summary>
public static class LyricsMatcher
{
    public static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ArrivalWindow = TimeSpan.FromSeconds(15);

    public static Candidate? Choose(
        Track track, IReadOnlyList<ResolvedCandidate> candidates, DateTimeOffset? trackStart = null)
    {
        var title = TrackIdentity.Normalize(track.Title);
        var artist = TrackIdentity.Normalize(TrackIdentity.SplitArtist(track.Artist));

        // Measured on a real session: SMTC reports EndTime=0 when a track
        // starts and fills it in about two seconds later. Zero therefore means
        // "not reported yet", not "zero seconds long". Gating on it rejected
        // every candidate (a 226s song sits 226s outside a 2s tolerance), and
        // because the failed attempt was cached, no new song ever matched.
        // When the length is unknown the name is the whole decision.
        var durationKnown = track.Duration > TimeSpan.Zero;

        var exact = candidates
            .Where(c => c.Info is not null
                        && TrackIdentity.Normalize(c.Info.Title) == title
                        && TrackIdentity.Normalize(c.Info.Artist) == artist)
            .OrderBy(c => Math.Abs((c.BodyDuration - track.Duration).TotalSeconds))
            .FirstOrDefault();

        if (exact is not null && (!durationKnown || WithinTolerance(exact, track)))
            return exact.Candidate;

        // Second tier: a file we cannot resolve by name, accepted only when it
        // appeared as this track started and its length agrees with SMTC.
        if (trackStart is not null)
        {
            var fresh = candidates
                .Where(c => c.Info is null)
                .Where(c => Math.Abs((c.WrittenAt - trackStart.Value).TotalSeconds) <= ArrivalWindow.TotalSeconds)
                .Where(c => !durationKnown || Math.Abs((c.BodyDuration - track.Duration).TotalSeconds) <= DurationTolerance.TotalSeconds)
                .OrderBy(c => Math.Abs((c.WrittenAt - trackStart.Value).TotalSeconds))
                .FirstOrDefault();

            if (fresh is not null)
                return fresh.Candidate;
        }

        return null;
    }

    private static bool WithinTolerance(ResolvedCandidate candidate, Track track)
        => Math.Abs((candidate.BodyDuration - track.Duration).TotalSeconds) <= DurationTolerance.TotalSeconds;
}
