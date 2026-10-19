using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LyricsResolverTests
{
    private const string DanTtml = """
    <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="5:06.000">
      <div><p begin="1" end="2">dan line one</p></div>
    </body></tt>
    """;

    private const string SpoiledTtml = """
    <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="5:06.066">
      <div><p begin="1" end="2">spoiled line one</p></div>
    </body></tt>
    """;

    /// <summary>Cache and catalog stubbed so resolution logic is tested without disk or network.</summary>
    private sealed class FakeCache : IAppleLyricsCache
    {
        public List<Candidate> Candidates { get; } = new();
        public IReadOnlyList<Candidate> Scan() => Candidates;
    }

    private sealed class FakeCatalog : ICatalogClient
    {
        public Dictionary<string, TrackInfo?> Answers { get; } = new();
        public (string Title, string Artist)? SearchQuery { get; private set; }
        public TrackInfo? SearchResult { get; set; }
        public Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct)
            => Task.FromResult(Answers.TryGetValue(lyricsId, out var v) ? v : null);
        public Task<TrackInfo?> SearchAsync(string title, string artist, CancellationToken ct)
        {
            SearchQuery = (title, artist);
            return Task.FromResult(SearchResult);
        }
    }

    [Fact]
    public async Task Resolves_by_title_and_artist_when_two_songs_have_near_identical_durations()
    {
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("dan.json", "AP_1872239911", DanTtml));
        cache.Candidates.Add(new Candidate("spoiled.json", "AP_1872239909", SpoiledTtml));

        // Dan's TTML body is nudged so it is the NEARER match to the played
        // 306.0s (the matcher orders by TTML body length, not the catalog's),
        // while the played title is Spoiled. Duration alone would therefore
        // resolve to the wrong song, and only the title key can reach the
        // asserted lyrics id. (The real pair is 305.718 vs 306.066, where
        // duration ordering happens to agree with the right answer; that
        // fixture could not detect a broken title comparison. Spec section 2.5
        // records the real measurements; this fixture perturbs them to be
        // load-bearing.)
        var catalog = new FakeCatalog();
        catalog.Answers["AP_1872239911"] = new TrackInfo("Dan", "Noah Kahan", TimeSpan.FromSeconds(306.0));
        catalog.Answers["AP_1872239909"] = new TrackInfo("Spoiled", "Noah Kahan", TimeSpan.FromSeconds(306.066));

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Spoiled", "Noah Kahan - The Great Divide", "The Great Divide", TimeSpan.FromSeconds(306.0), true);

        var doc = await resolver.ResolveAsync(track, CancellationToken.None);

        Assert.NotNull(doc);
        Assert.Equal("AP_1872239909", doc!.LyricsId);
        Assert.Equal("spoiled line one", doc.Lines[0].Text);
    }

    [Fact]
    public async Task Resolves_an_unresolvable_file_through_search_when_durations_agree()
    {
        // Real session: "Ini Abadi" by Perunggu was cached under an id the
        // lookup could not resolve, but the iTunes search found the track and
        // its duration matched the TTML body exactly. The search names the
        // file, and the matcher then matches it by name.
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("ini-abadi.json", "AP_1610259020", """
        <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="3:52.000">
          <div><p begin="1" end="2">ini abadi line</p></div>
        </body></tt>
        """));

        var catalog = new FakeCatalog
        {
            SearchResult = new TrackInfo("Ini Abadi", "Perunggu", TimeSpan.FromSeconds(232)),
        };

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Ini Abadi", "Perunggu — Memorandum", "Memorandum", TimeSpan.Zero, true);

        var doc = await resolver.ResolveAsync(track, CancellationToken.None);

        Assert.NotNull(doc);
        Assert.Equal("AP_1610259020", doc!.LyricsId);
        Assert.Equal("ini abadi line", doc.Lines[0].Text);
        Assert.Equal(("Ini Abadi", "Perunggu"), catalog.SearchQuery);
    }

    [Fact]
    public async Task Search_fallback_does_not_adopt_a_file_whose_duration_disagrees()
    {
        // Real session: "Biang Lara" by Perunggu was NEVER cached by Apple,
        // but a .Feast file with body 3:53 sat in the cache, 7s from Biang
        // Lara's catalog duration of 4:00. A loose tolerance adopted it and
        // the user saw the wrong lyrics — worse than no lyrics. Duration must
        // gate the fallback strictly: no file near enough means no lyrics.
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("feast.json", "MX_33457682-38992209", """
        <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="3:53.360">
          <div><p begin="1" end="2">jika bisa unduh mobil</p></div>
        </body></tt>
        """));

        var catalog = new FakeCatalog
        {
            SearchResult = new TrackInfo("Biang Lara", "Perunggu", TimeSpan.FromMilliseconds(240469)),
        };

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Biang Lara", "Perunggu — Memorandum", "Memorandum", TimeSpan.Zero, true);

        Assert.Null(await resolver.ResolveAsync(track, CancellationToken.None));
    }

    [Fact]
    public async Task Search_fallback_adopts_the_single_candidate_nearest_by_duration()
    {
        // When two unresolvable files both sit inside the tolerance, the info
        // goes to exactly one — the nearest by duration — never to both.
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("neighbor.json", "MX_neighbor", """
        <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="3:52.000">
          <div><p begin="1" end="2">neighbor line</p></div>
        </body></tt>
        """));
        cache.Candidates.Add(new Candidate("true.json", "MX_true", """
        <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="4:00.300">
          <div><p begin="1" end="2">true line</p></div>
        </body></tt>
        """));

        var catalog = new FakeCatalog
        {
            SearchResult = new TrackInfo("Some Song", "Someone", TimeSpan.FromMilliseconds(240469)),
        };

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Some Song", "Someone — Album", "Album", TimeSpan.Zero, true);

        var doc = await resolver.ResolveAsync(track, CancellationToken.None);

        Assert.NotNull(doc);
        Assert.Equal("MX_true", doc!.LyricsId);
        Assert.Equal("true line", doc.Lines[0].Text);
    }

    [Fact]
    public async Task Returns_null_when_nothing_matches()
    {
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("dan.json", "AP_1872239911", DanTtml));
        var catalog = new FakeCatalog();
        catalog.Answers["AP_1872239911"] = new TrackInfo("Dan", "Noah Kahan", TimeSpan.FromSeconds(305.718));

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Unrelated", "Nobody", "X", TimeSpan.FromSeconds(200), true);

        Assert.Null(await resolver.ResolveAsync(track, CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_body_duration_is_a_miss_not_a_crash()
    {
        // A garbage dur used to escape BodyDuration as a FormatException from
        // outside ResolveAsync's try, breaking its own contract ("a malformed
        // document is a miss, not a crash"). Either a null result or a parsed
        // document is acceptable here; a thrown exception is the failure.
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("bad-dur.json", "AP_1872239909", """
        <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="not-a-time">
          <div><p begin="1" end="2">spoiled line one</p></div>
        </body></tt>
        """));

        var catalog = new FakeCatalog();
        catalog.Answers["AP_1872239909"] = new TrackInfo("Spoiled", "Noah Kahan", TimeSpan.FromSeconds(306.066));

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Spoiled", "Noah Kahan - The Great Divide", "The Great Divide", TimeSpan.FromSeconds(306.066), true);

        var doc = await resolver.ResolveAsync(track, CancellationToken.None);

        Assert.True(doc is null || doc.Lines.Count > 0);
    }
}
