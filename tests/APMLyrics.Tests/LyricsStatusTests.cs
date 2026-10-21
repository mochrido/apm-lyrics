using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LyricsStatusTests
{
    private static LyricsDoc Doc() => new("id", null, new[]
    {
        new LyricLine(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "line", null),
    });

    [Fact]
    public void A_synced_document_needs_no_status_line()
        => Assert.Null(LyricsStatus.For(new LyricsResolution(LyricsResolutionKind.Synced, Doc())));

    [Fact]
    public void A_synced_kind_without_a_timed_document_is_not_renderable()
    {
        // A Timing=None document may never be highlighted, so it must fall back
        // to a status even if a caller labels it Synced by mistake.
        var unsyncedDoc = Doc() with { Timing = LyricsTiming.None };
        Assert.Equal(
            LyricsStatus.NoMatch,
            LyricsStatus.For(new LyricsResolution(LyricsResolutionKind.Synced, unsyncedDoc)));
    }

    [Fact]
    public void Loading_says_it_is_still_looking()
        => Assert.Equal(LyricsStatus.Finding, LyricsStatus.For(new LyricsResolution(LyricsResolutionKind.Loading)));

    [Fact]
    public void Unsynced_only_is_stated_plainly()
        => Assert.Equal(LyricsStatus.OnlyUnsynced, LyricsStatus.For(new LyricsResolution(LyricsResolutionKind.Unsynced)));

    [Fact]
    public void An_offline_check_is_distinct_from_a_miss()
    {
        Assert.Equal(LyricsStatus.Offline, LyricsStatus.For(new LyricsResolution(LyricsResolutionKind.NetworkUnavailable)));
        Assert.Equal(LyricsStatus.NoMatch, LyricsStatus.For(new LyricsResolution(LyricsResolutionKind.NotFound)));
    }

    [Fact]
    public void Pending_duration_reads_as_a_miss_if_it_ever_reaches_the_ui()
        => Assert.Equal(LyricsStatus.NoMatch, LyricsStatus.For(new LyricsResolution(LyricsResolutionKind.PendingDuration)));
}
