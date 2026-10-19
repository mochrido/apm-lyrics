using System.Net;
using System.Net.Http;
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class GitHubReleaseFeedTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/mochrido/apm-lyrics/releases/tag/v1.2.0",
          "assets": [
            { "name": "checksums.txt", "browser_download_url": "https://example.com/checksums.txt" },
            { "name": "APMLyricsSetup.exe", "browser_download_url": "https://example.com/APMLyricsSetup.exe" }
          ]
        }
        """;

    private static HttpClient Client(HttpStatusCode status, string body) => new(
        new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }))
        { BaseAddress = new Uri("https://api.github.com/") };

    [Fact]
    public async Task Returns_tag_and_setup_url()
    {
        var feed = new GitHubReleaseFeed(Client(HttpStatusCode.OK, ReleaseJson));
        var release = await feed.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("v1.2.0", release!.Tag);
        Assert.Equal("https://example.com/APMLyricsSetup.exe", release.SetupAssetUrl);
        Assert.Equal("https://github.com/mochrido/apm-lyrics/releases/tag/v1.2.0", release.ReleasePageUrl);
    }

    [Fact]
    public async Task Null_on_http_failure()
    {
        var feed = new GitHubReleaseFeed(Client(HttpStatusCode.NotFound, "{}"));
        Assert.Null(await feed.GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Null_on_garbage_json()
    {
        var feed = new GitHubReleaseFeed(Client(HttpStatusCode.OK, "not json at all"));
        Assert.Null(await feed.GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Null_when_no_setup_asset()
    {
        var json = """
            { "tag_name": "v1.2.0", "html_url": "https://example.com/release",
              "assets": [ { "name": "other.zip", "browser_download_url": "https://example.com/other.zip" } ] }
            """;
        var feed = new GitHubReleaseFeed(Client(HttpStatusCode.OK, json));
        Assert.Null(await feed.GetLatestAsync(CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
