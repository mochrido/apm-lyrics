using System.Net;
using System.Net.Http;
using System.Text;
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LrclibClientTests
{
    private static readonly Track Song = new(
        "Pasti Ada Jalan",
        "Perunggu - Pasti Ada Jalan - Single",
        "Pasti Ada Jalan",
        TimeSpan.FromSeconds(139),
        IsPlaying: true);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Candidate(
        long id,
        string title,
        string artist,
        string album,
        int duration,
        string? synced = "[00:00.02]line one\n[00:05.00]line two",
        string? plain = "line one\nline two")
    {
        static string? Q(string? s) => s is null ? "null" : $"\"{s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"")}\"";
        return $$"""
        {"id":{{id}},"trackName":{{Q(title)}},"artistName":{{Q(artist)}},"albumName":{{Q(album)}},
         "duration":{{duration}},"instrumental":false,"plainLyrics":{{Q(plain)}},"syncedLyrics":{{Q(synced)}}}
        """;
    }

    private static LrclibClient Client(StubHandler handler)
        => new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) });

    private static StubHandler Searching(params string[] candidates)
        => new(_ => Json("[" + string.Join(",", candidates) + "]"));

    private static string[] LineOne()
    {
        var candidates = new[]
        {
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 281),
            Candidate(2, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 30),
            Candidate(3, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 2),
            Candidate(4, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139),
        };
        return candidates;
    }

    [Fact]
    public async Task Selects_the_duration_matching_candidate_not_the_first()
    {
        var client = Client(Searching(LineOne()));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
        Assert.NotNull(result.Document);
        Assert.Equal(2, result.Document!.Lines.Count);
        Assert.Equal("lrclib.net:4", result.Document.LyricsId);
    }

    [Fact]
    public async Task Unknown_duration_is_pending_not_a_guess()
    {
        var client = Client(Searching(LineOne()));
        var noDuration = Song with { Duration = TimeSpan.Zero };

        var result = await client.ResolveAsync(noDuration, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.PendingDuration, result.Kind);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task Two_second_delta_is_accepted()
    {
        var candidate = Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 141);
        var client = Client(Searching(candidate));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
    }

    [Fact]
    public async Task Delta_beyond_two_seconds_is_rejected()
    {
        var candidate = Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 142);
        var client = Client(Searching(candidate));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Wrong_artist_is_rejected_even_at_the_right_duration()
    {
        var candidate = Candidate(1, "Pasti Ada Jalan", "Somebody Else", "Pasti Ada Jalan", 139);
        var client = Client(Searching(candidate));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Wrong_title_is_rejected_even_at_the_right_duration()
    {
        var candidate = Candidate(1, "Pasti Ada Jalan (Reprise)", "Perunggu", "Pasti Ada Jalan", 139);
        var client = Client(Searching(candidate));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Live_version_is_rejected_for_a_studio_recording()
    {
        var candidate = Candidate(1, "Pasti Ada Jalan (Live)", "Perunggu", "Pasti Ada Jalan", 139);
        var client = Client(Searching(candidate));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Live_track_prefers_a_live_candidate_over_a_studio_one()
    {
        var liveTrack = Song with { Title = "Pasti Ada Jalan (Live)" };
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139),
            Candidate(2, "Pasti Ada Jalan (Live)", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]live line")));

        var result = await client.ResolveAsync(liveTrack, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
        Assert.Equal("lrclib.net:2", result.Document!.LyricsId);
    }

    [Fact]
    public async Task Agreeing_album_beats_a_conflicting_album()
    {
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "2:20", 139, synced: "[00:00.02]other album line"),
            Candidate(2, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]right album line")));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal("lrclib.net:2", result.Document!.LyricsId);
    }

    [Fact]
    public async Task Missing_album_does_not_invent_agreement()
    {
        var noAlbum = Song with { Album = string.Empty };
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]a"),
            Candidate(2, "Pasti Ada Jalan", "Perunggu", "2:20", 139, synced: "[00:00.02]b")));

        var result = await client.ResolveAsync(noAlbum, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Equally_ranked_different_payloads_are_ambiguous()
    {
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]first payload"),
            Candidate(2, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]second payload")));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Equally_ranked_equivalent_payloads_collapse()
    {
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]line one\n[00:05.00]line two"),
            Candidate(2, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.020]line one\r\n[00:05.000]line two")));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
    }

    [Fact]
    public async Task Plain_only_match_reports_unsynced_not_synced()
    {
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: null)));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Unsynced, result.Kind);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task Plain_first_does_not_hide_a_synced_candidate()
    {
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: null),
            Candidate(2, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]timed line")));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
        Assert.Equal("lrclib.net:2", result.Document!.LyricsId);
    }

    [Fact]
    public async Task Malformed_first_does_not_hide_a_synced_candidate()
    {
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "not timed at all", plain: "not timed at all"),
            Candidate(2, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]timed line")));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
        Assert.Equal("lrclib.net:2", result.Document!.LyricsId);
    }

    [Fact]
    public async Task Synced_field_that_is_unparseable_is_a_miss()
    {
        var client = Client(Searching(
            Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139, synced: "[00:00.02]good\nplain words", plain: null)));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task Server_error_is_network_unavailable()
    {
        var client = Client(new StubHandler(_ => Json("overloaded", HttpStatusCode.ServiceUnavailable)));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NetworkUnavailable, result.Kind);
    }

    [Fact]
    public async Task Too_many_requests_retries_once_then_succeeds()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                var busy = Json("slow down", HttpStatusCode.TooManyRequests);
                busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return busy;
            }
            return Json("[" + Candidate(1, "Pasti Ada Jalan", "Perunggu", "Pasti Ada Jalan", 139) + "]");
        });
        var client = Client(handler);

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = Client(Searching(LineOne()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ResolveAsync(Song, cts.Token));
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        var big = "[" + new string(' ', 3 * 1024 * 1024) + "]";
        var client = Client(new StubHandler(_ => Json(big)));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NetworkUnavailable, result.Kind);
    }

    [Fact]
    public async Task Malformed_json_is_a_miss_not_a_crash()
    {
        var client = Client(new StubHandler(_ => Json("{not json")));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Empty_result_set_is_not_found()
    {
        var client = Client(new StubHandler(_ => Json("[]")));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Null_metadata_entries_are_ignored()
    {
        var client = Client(new StubHandler(_ => Json("""
        [{"id":1,"trackName":null,"artistName":null,"albumName":null,"duration":null,
          "plainLyrics":null,"syncedLyrics":null}]
        """)));

        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Query_is_url_encoded_and_identifies_the_app()
    {
        var handler = Searching();
        var client = Client(handler);

        await client.ResolveAsync(Song, CancellationToken.None);

        var uri = Assert.Single(handler.Requests);
        Assert.Contains("lrclib.net/api/search", uri.ToString());
        Assert.Contains("track_name=Pasti%20Ada%20Jalan", uri.Query);
        Assert.Contains("artist_name=Perunggu", uri.Query);
    }

    [Fact]
    public async Task Request_sends_a_descriptive_user_agent()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            return Json("[]");
        });
        var client = new LrclibClient(new HttpClient(handler));

        await client.ResolveAsync(Song, CancellationToken.None);

        Assert.NotNull(seen);
        var agent = seen!.Headers.UserAgent.ToString();
        Assert.Contains("APMLyrics", agent);
        Assert.DoesNotContain("HttpClient", agent, StringComparison.OrdinalIgnoreCase);
    }
}
