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
        public Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct)
            => Task.FromResult(Answers.TryGetValue(lyricsId, out var v) ? v : null);
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
}
