using System.IO;
using System.Net;
using System.Net.Http;
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class CatalogClientTests
{
    private class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private bool _disposed;
        public int Calls { get; private set; }
        public StubHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Real handlers (HttpClientHandler, SocketsHttpHandler) throw after
            // disposal; a stub that keeps serving would hide that class of bug.
            if (_disposed)
                throw new ObjectDisposedException(nameof(StubHandler));

            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body),
            });
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "catalog-lookup.json");

    private static string Body => File.ReadAllText(FixturePath);

    [Theory]
    [InlineData("AP_1872239909", "1872239909")]
    [InlineData("MX_46242766-48516696", null)]
    [InlineData("", null)]
    [InlineData("AP_", null)]
    public void Extracts_a_song_id_only_from_the_ap_form(string lyricsId, string? expected)
    {
        Assert.Equal(expected, CatalogClient.SongIdFromLyricsId(lyricsId));
    }

    [Fact]
    public async Task Parses_a_lookup_response()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var client = new CatalogClient(new StubHandler(Body), cache);
            var info = await client.LookupAsync("AP_1872239909", CancellationToken.None);

            Assert.NotNull(info);
            Assert.Equal("Spoiled", info!.Title);
            Assert.Equal("Noah Kahan", info.Artist);
            Assert.Equal(306.066, info.Duration.TotalSeconds, 3);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_second_lookup_is_served_from_disk_without_a_request()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var handler = new StubHandler(Body);
            var client = new CatalogClient(handler, cache);

            await client.LookupAsync("AP_1872239909", CancellationToken.None);
            var second = await client.LookupAsync("AP_1872239909", CancellationToken.None);

            Assert.Equal(1, handler.Calls); // warm cache: offline after first lookup
            Assert.Equal("Spoiled", second!.Title);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_network_failure_returns_null_and_does_not_throw()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var client = new CatalogClient(new ThrowingHandler(), cache);
            var info = await client.LookupAsync("AP_1872239909", CancellationToken.None);
            Assert.Null(info);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_network_failure_still_serves_a_warm_entry()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var warm = new CatalogClient(new StubHandler(Body), cache);
            await warm.LookupAsync("AP_1872239909", CancellationToken.None);

            var cold = new CatalogClient(new ThrowingHandler(), cache);
            var info = await cold.LookupAsync("AP_1872239909", CancellationToken.None);

            Assert.Equal("Spoiled", info!.Title);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task An_empty_result_set_returns_null()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var client = new CatalogClient(new StubHandler("""{"resultCount":0,"results":[]}"""), cache);
            Assert.Null(await client.LookupAsync("AP_999", CancellationToken.None));
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_failed_lookup_is_not_remembered_so_it_can_succeed_later()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var offline = new CatalogClient(new ThrowingHandler(), cache);
            Assert.Null(await offline.LookupAsync("AP_1872239909", CancellationToken.None));

            // A fresh client over the SAME cache file: the failed lookup must not
            // have been written to disk as a permanent null, so going back online
            // still resolves the track instead of serving the stored failure.
            var handler = new StubHandler(Body);
            var online = new CatalogClient(handler, cache);
            var info = await online.LookupAsync("AP_1872239909", CancellationToken.None);

            Assert.NotNull(info);
            Assert.Equal("Spoiled", info!.Title);
            Assert.Equal(1, handler.Calls); // the network was tried again
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_completed_lookup_with_no_result_is_not_looked_up_again()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var handler = new StubHandler("""{"resultCount":0,"results":[]}""");
            var client = new CatalogClient(handler, cache);

            Assert.Null(await client.LookupAsync("AP_1872239909", CancellationToken.None));
            Assert.Null(await client.LookupAsync("AP_1872239909", CancellationToken.None));

            // A completed lookup that found nothing IS remembered: that is the
            // negative cache. MX_ ids never resolve, and retrying them over HTTP
            // on every track change would make the offline path worse.
            Assert.Equal(1, handler.Calls);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task One_injected_handler_keeps_working_across_lookups()
    {
        // The client must not dispose the handler it was handed: that handler is
        // the caller's seam, and disposing it would make every later lookup for a
        // different id fail with ObjectDisposedException.
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var handler = new StubHandler(Body);
            var client = new CatalogClient(handler, cache);

            Assert.NotNull(await client.LookupAsync("AP_1872239909", CancellationToken.None));
            Assert.NotNull(await client.LookupAsync("AP_1872239911", CancellationToken.None));

            Assert.Equal(2, handler.Calls);
        }
        finally { File.Delete(cache); }
    }

    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }
}
