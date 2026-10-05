using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LyricsMatcherTests
{
    private static ResolvedCandidate Cand(
        string id, string? title, string? artist, double bodySeconds,
        DateTimeOffset? written = null, string path = "c.json")
    {
        var info = title is null ? null : new TrackInfo(title, artist!, TimeSpan.FromSeconds(bodySeconds));
        return new ResolvedCandidate(
            new Candidate(path, id, "<tt/>"), info, TimeSpan.FromSeconds(bodySeconds),
            written ?? DateTimeOffset.UnixEpoch);
    }

    private static Track Track(string title, string artist, double seconds, string album = "A")
        => new(title, artist, album, TimeSpan.FromSeconds(seconds), true);

    [Fact]
    public void Splits_the_album_suffix_out_of_the_artist_field()
    {
        Assert.Equal("Noah Kahan", TrackIdentity.SplitArtist("Noah Kahan - The Great Divide: The Last Of The Bugs"));
    }

    [Fact]
    public void Leaves_a_plain_artist_untouched()
    {
        Assert.Equal("Noah Kahan", TrackIdentity.SplitArtist("Noah Kahan"));
    }

    [Fact]
    public void Normalizes_case_whitespace_and_decorations()
    {
        Assert.Equal("the great divide", TrackIdentity.Normalize("  The   Great Divide  - Single "));
    }

    [Fact]
    public void Picks_the_title_and_artist_match_even_when_durations_collide()
    {
        // Spec section 2.5: Dan 305.718 and Spoiled 306.066 are 0.35s apart.
        // Dan is deliberately the NEARER match to the played duration (306.0),
        // so duration alone would choose the wrong song. Only the title
        // comparison can pick Spoiled, which is what makes it load-bearing.
        var candidates = new[]
        {
            Cand("AP_1872239911", "Dan", "Noah Kahan", 306.0),
            Cand("AP_1872239909", "Spoiled", "Noah Kahan", 306.066),
        };
        var track = Track("Spoiled", "Noah Kahan - The Great Divide", 306.0);

        var chosen = LyricsMatcher.Choose(track, candidates);

        Assert.NotNull(chosen);
        Assert.Equal("AP_1872239909", chosen!.LyricsId);
    }

    [Fact]
    public void Uses_duration_only_to_break_a_title_tie()
    {
        // AP_b is FIRST in the list but AP_a is NEARER by duration. Reversing
        // input order against duration order is what makes the OrderBy
        // load-bearing: without it, FirstOrDefault would return AP_b.
        var candidates = new[]
        {
            Cand("AP_b", "Dashboard", "Noah Kahan", 231.0),
            Cand("AP_a", "Dashboard", "Noah Kahan", 230.787),
        };
        var track = Track("Dashboard", "Noah Kahan", 230.8);

        var chosen = LyricsMatcher.Choose(track, candidates);

        Assert.Equal("AP_a", chosen!.LyricsId);
    }

    [Fact]
    public void Ignores_a_title_match_with_the_wrong_artist()
    {
        var candidates = new[] { Cand("AP_a", "Dashboard", "Someone Else", 230.0) };
        var track = Track("Dashboard", "Noah Kahan", 230.0);

        Assert.Null(LyricsMatcher.Choose(track, candidates));
    }

    [Fact]
    public void Falls_back_to_a_freshly_written_unresolvable_file()
    {
        var trackStart = DateTimeOffset.Parse("2026-10-05T14:00:00Z");
        var candidates = new[]
        {
            Cand("MX_46242766-48516696", null, null!, 258.010, written: trackStart.AddSeconds(3)),
        };
        var track = Track("Some Song", "Someone", 258.0);

        var chosen = LyricsMatcher.Choose(track, candidates, trackStart);

        Assert.Equal("MX_46242766-48516696", chosen!.LyricsId);
    }

    [Fact]
    public void Rejects_a_stale_unresolvable_file()
    {
        var trackStart = DateTimeOffset.Parse("2026-10-05T14:00:00Z");
        var candidates = new[]
        {
            Cand("MX_old", null, null!, 258.0, written: trackStart.AddMinutes(-30)),
        };
        var track = Track("Some Song", "Someone", 258.0);

        Assert.Null(LyricsMatcher.Choose(track, candidates, trackStart));
    }

    [Fact]
    public void Returns_null_when_nothing_matches_so_stale_lyrics_are_never_reused()
    {
        var candidates = new[] { Cand("AP_a", "Other Song", "Noah Kahan", 100.0) };
        var track = Track("Dashboard", "Noah Kahan", 230.0);

        Assert.Null(LyricsMatcher.Choose(track, candidates));
    }

    [Fact]
    public void Tolerates_a_small_duration_difference_between_smtc_and_ttml()
    {
        var candidates = new[] { Cand("AP_a", "Dashboard", "Noah Kahan", 230.787) };
        var track = Track("Dashboard", "Noah Kahan", 232.5);

        Assert.NotNull(LyricsMatcher.Choose(track, candidates));
    }
}
