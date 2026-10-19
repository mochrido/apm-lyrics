using System.IO;
namespace APMLyrics.Core;

/// <summary>
/// Turns a playing track into lyrics: scan the cache, identify each candidate,
/// let the matcher choose, then parse the winner. Never returns stale lyrics:
/// no match means null, and the caller shows its empty state.
/// </summary>
public sealed class LyricsResolver
{
    private readonly IAppleLyricsCache _cache;
    private readonly ICatalogClient _catalog;

    // The search fallback's duration gate is the matcher's own tolerance: a
    // file whose body length only "roughly" fits was measured adopting the
    // WRONG song (.Feast at 3:53 shown for Biang Lara at 4:00), which is worse
    // than an honest "no lyrics". Only a near-exact fit may be named.
    internal static readonly TimeSpan SearchDurationTolerance = LyricsMatcher.DurationTolerance;

    public LyricsResolver(IAppleLyricsCache cache, ICatalogClient catalog)
    {
        _cache = cache;
        _catalog = catalog;
    }

    public async Task<LyricsDoc?> ResolveAsync(Track track, CancellationToken ct, DateTimeOffset? trackStart = null)
    {
        var candidates = _cache.Scan();
        if (candidates.Count == 0)
            return null;

        var resolved = new List<ResolvedCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var info = await _catalog.LookupAsync(candidate.LyricsId, ct);
            resolved.Add(new ResolvedCandidate(candidate, info, BodyDuration(candidate.Ttml), WrittenAt(candidate.FilePath)));
        }

        await AttachSearchInfoAsync(track, resolved, ct);

        var chosen = LyricsMatcher.Choose(track, resolved, trackStart);
        if (chosen is null)
            return null;

        try
        {
            return TtmlParser.Parse(chosen.LyricsId, chosen.Ttml);
        }
        catch (FormatException)
        {
            return null; // a malformed document is a miss, not a crash
        }
    }

    /// <summary>
    /// Names the unnameable: for candidates whose id the catalog cannot look
    /// up (MX_, unknown forms), the playing track's own name identifies the
    /// song through the search API. Apple prefetches some lyric files minutes
    /// before the track starts, so the matcher's timing window cannot reach
    /// them; the search names them instead.
    ///
    /// The searched info is attached to exactly ONE candidate — the one whose
    /// body duration is nearest the searched duration — because attaching it
    /// to every candidate inside the tolerance let a same-length neighbour
    /// ("Ini Abadi", 3:52, next to "Biang Lara", 3:53) be renamed into the
    /// playing track and win the match.
    /// </summary>
    private async Task AttachSearchInfoAsync(Track track, List<ResolvedCandidate> resolved, CancellationToken ct)
    {
        var unnamed = resolved.Where(r => r.Info is null).ToList();
        if (unnamed.Count == 0)
            return;

        var searched = await _catalog.SearchAsync(track.Title, TrackIdentity.SplitArtist(track.Artist), ct);
        if (searched is null)
            return;

        // Nearest by duration wins; ties are ambiguous, so nobody is named.
        ResolvedCandidate? best = null;
        var bestGap = double.MaxValue;
        var tie = false;
        foreach (var c in unnamed)
        {
            var gap = Math.Abs((c.BodyDuration - searched.Duration).TotalSeconds);
            if (gap > SearchDurationTolerance.TotalSeconds)
                continue;
            if (gap < bestGap)
            {
                bestGap = gap;
                best = c;
                tie = false;
            }
            else if (gap == bestGap)
            {
                tie = true;
            }
        }

        if (best is not null && !tie)
            resolved[resolved.IndexOf(best)] = best with { Info = searched };
    }

    internal static TimeSpan BodyDuration(string ttml)
    {
        var marker = "<body";
        var start = ttml.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return TimeSpan.Zero;

        var end = ttml.IndexOf('>', start);
        if (end < 0)
            return TimeSpan.Zero;

        var header = ttml[start..end];
        var durIndex = header.IndexOf("dur=\"", StringComparison.Ordinal);
        if (durIndex < 0)
            return TimeSpan.Zero;

        var valueStart = durIndex + 5;
        var valueEnd = header.IndexOf('"', valueStart);
        if (valueEnd < 0)
            return TimeSpan.Zero;

        try
        {
            return TtmlParser.ParseTime(header[valueStart..valueEnd]);
        }
        catch (Exception)
        {
            // A garbage or out-of-range dur is a miss, not a crash: same Zero as
            // every other unparseable shape above, so ResolveAsync keeps its
            // contract that a malformed document never throws.
            return TimeSpan.Zero;
        }
    }

    private static DateTimeOffset WrittenAt(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }
}
