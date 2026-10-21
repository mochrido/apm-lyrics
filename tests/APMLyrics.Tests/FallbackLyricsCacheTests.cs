using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class FallbackLyricsCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "apmlyrics-cache-tests", Guid.NewGuid().ToString("N"));

    private static readonly Track Song = new(
        "Pasti Ada Jalan",
        "Perunggu - Pasti Ada Jalan - Single",
        "Pasti Ada Jalan",
        TimeSpan.FromSeconds(139),
        IsPlaying: true);

    private static readonly LrclibCandidate Candidate = new(
        17312337,
        "Pasti Ada Jalan",
        "Perunggu",
        "Pasti Ada Jalan",
        TimeSpan.FromSeconds(139),
        "[00:00.02]line one\n[00:05.00]line two",
        "line one\nline two");

    private static LyricsDoc Doc()
        => LrcParser.Parse("lrclib.net:17312337", Candidate.SyncedLyrics!, TimeSpan.FromSeconds(139));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void Accepted_synced_result_round_trips()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        var read = cache.Read(Song);

        Assert.NotNull(read);
        Assert.Equal(LyricsTiming.Line, read!.Timing);
        Assert.Equal(2, read.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(0.02), read.Lines[0].Begin);
        Assert.Equal("line one", read.Lines[0].Text);
        Assert.Equal("lrclib.net:17312337", read.LyricsId);
    }

    [Fact]
    public void Another_recording_cannot_reuse_the_entry()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        var live = Song with { Title = "Pasti Ada Jalan (Live)" };
        Assert.Null(cache.Read(live));

        var otherArtist = Song with { Artist = "Somebody Else" };
        Assert.Null(cache.Read(otherArtist));
    }

    [Fact]
    public void Another_duration_cannot_reuse_the_entry()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        Assert.Null(cache.Read(Song with { Duration = TimeSpan.FromSeconds(281) }));
    }

    [Fact]
    public void Unknown_duration_does_not_reuse_an_entry()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        Assert.Null(cache.Read(Song with { Duration = TimeSpan.Zero }));
    }

    [Fact]
    public void Unknown_duration_is_not_cached()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song with { Duration = TimeSpan.Zero }, Candidate, Doc());

        Assert.Null(cache.Read(Song with { Duration = TimeSpan.Zero }));
        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public void Pause_state_does_not_change_the_key()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song with { IsPlaying = true }, Candidate, Doc());

        Assert.NotNull(cache.Read(Song with { IsPlaying = false }));
    }

    [Fact]
    public void Key_is_stable_across_process_restarts()
    {
        var first = new FallbackLyricsCache(_dir);
        first.Write(Song, Candidate, Doc());

        var second = new FallbackLyricsCache(_dir);
        Assert.NotNull(second.Read(Song));
    }

    [Fact]
    public void Corrupt_json_returns_null()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        foreach (var file in Directory.GetFiles(_dir, "*.json"))
            File.WriteAllText(file, "{ \"schema\": 1, ");

        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public void Truncated_json_returns_null()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        foreach (var file in Directory.GetFiles(_dir, "*.json"))
            File.WriteAllText(file, string.Empty);

        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public void Unsupported_schema_returns_null()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        foreach (var file in Directory.GetFiles(_dir, "*.json"))
            File.WriteAllText(file, File.ReadAllText(file).Replace("\"schema\": 1", "\"schema\": 99"));

        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public void Missing_directory_reads_as_a_miss()
    {
        var cache = new FallbackLyricsCache(Path.Combine(_dir, "does", "not", "exist"));
        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public void A_failed_write_does_not_destroy_the_existing_entry()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());
        var before = cache.Read(Song);
        Assert.NotNull(before);

        // A document that cannot be serialized as timed lines (a bad timing
        // value) must be refused without touching the good entry on disk.
        var broken = Doc() with
        {
            Lines = new[] { new LyricLine(TimeSpan.FromTicks(-1), TimeSpan.Zero, "bad", null) },
        };

        cache.Write(Song, Candidate, broken);

        var after = cache.Read(Song);
        Assert.NotNull(after);
        Assert.Equal(before!.Lines.Count, after!.Lines.Count);
    }

    [Fact]
    public void Timestamps_are_revalidated_on_read()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        foreach (var file in Directory.GetFiles(_dir, "*.json"))
        {
            var json = File.ReadAllText(file);
            File.WriteAllText(file, json.Replace("\"beginSeconds\": 0.02", "\"beginSeconds\": 5000"));
        }

        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public void Unsynced_documents_are_not_cached_as_synced()
    {
        var cache = new FallbackLyricsCache(_dir);
        var unsynced = new LyricsDoc("lrclib.net:1", null, Array.Empty<LyricLine>())
        {
            Timing = LyricsTiming.None,
        };

        cache.Write(Song, Candidate, unsynced);

        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public async Task Client_uses_the_cache_before_touching_the_network()
    {
        var handler = new CountingHandler();
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        var client = new LrclibClient(new HttpClient(handler), cache);
        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
        Assert.Equal(0, handler.Calls);
        Assert.Equal("LRCLIB cache", result.Source);
    }

    [Fact]
    public async Task A_network_success_is_written_to_the_cache()
    {
        var body = """
        [{"id":17312337,"trackName":"Pasti Ada Jalan","artistName":"Perunggu",
          "albumName":"Pasti Ada Jalan","duration":139,
          "plainLyrics":"line one\nline two","syncedLyrics":"[00:00.02]line one\n[00:05.00]line two"}]
        """;
        var handler = new CountingHandler(body);
        var cache = new FallbackLyricsCache(_dir);

        var client = new LrclibClient(new HttpClient(handler), cache);
        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.Synced, result.Kind);
        Assert.NotNull(cache.Read(Song));
    }

    [Fact]
    public async Task Network_failure_with_no_cache_is_network_unavailable()
    {
        var handler = new CountingHandler(status: HttpStatusCode.ServiceUnavailable);
        var cache = new FallbackLyricsCache(_dir);

        var client = new LrclibClient(new HttpClient(handler), cache);
        var result = await client.ResolveAsync(Song, CancellationToken.None);

        Assert.Equal(LyricsResolutionKind.NetworkUnavailable, result.Kind);
        Assert.Null(cache.Read(Song));
    }

    [Fact]
    public void Filenames_do_not_leak_the_track_title()
    {
        var cache = new FallbackLyricsCache(_dir);
        cache.Write(Song, Candidate, Doc());

        foreach (var file in Directory.GetFiles(_dir))
        {
            var name = Path.GetFileName(file);
            Assert.DoesNotContain("pasti", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("perunggu", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string? _body;
        private readonly HttpStatusCode _status;

        public CountingHandler(string? body = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body ?? "[]", Encoding.UTF8, "application/json"),
            });
        }
    }
}
