using System.Net;
using System.Net.Http;
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class UpdateServiceTests
{
    private sealed class StubFeed(ReleaseInfo? result) : IReleaseFeed
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct) => Task.FromResult(result);
    }

    private static ReleaseInfo Newer() => new("v9.9.9", "https://example.com/APMLyricsSetup.exe",
        "https://github.com/mochrido/apm-lyrics/releases/tag/v9.9.9");

    private static HttpClient DownloadClient(HttpStatusCode status, byte[] body) => new(
        new StubHandler(_ => new HttpResponseMessage(status) { Content = new ByteArrayContent(body) }));

    [Fact]
    public async Task CheckAsync_raises_UpdateAvailable_for_newer_tag()
    {
        ReleaseInfo? raised = null;
        var service = new UpdateService(new StubFeed(Newer()), () => new Version(1, 0, 0));
        service.UpdateAvailable += r => raised = r;

        var found = await service.CheckAsync(CancellationToken.None);

        Assert.True(found);
        Assert.Equal("v9.9.9", raised?.Tag);
        Assert.Equal(UpdateState.Available, service.State);
    }

    [Fact]
    public async Task CheckAsync_false_when_no_update_or_offline()
    {
        var upToDate = new UpdateService(new StubFeed(new ReleaseInfo("v1.0.0", null, "https://x")),
            () => new Version(1, 0, 0));
        Assert.False(await upToDate.CheckAsync(CancellationToken.None));

        var offline = new UpdateService(new StubFeed(null), () => new Version(1, 0, 0));
        Assert.False(await offline.CheckAsync(CancellationToken.None));
        Assert.Equal(UpdateState.Idle, offline.State);
    }

    [Fact]
    public async Task DownloadAndRun_refused_when_not_available()
    {
        var launched = new List<(string Path, string Args)>();
        var service = new UpdateService(new StubFeed(Newer()), () => new Version(1, 0, 0))
        {
            Launcher = (path, args) => { launched.Add((path, args)); return true; },
        };

        // No successful check first: state is Idle, so the download must not start.
        var ok = await service.DownloadAndRunInstallerAsync(Newer(), CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(launched);
        Assert.Equal(UpdateState.Idle, service.State);
    }

    [Fact]
    public async Task DownloadAndRun_launches_installer_with_silent_flags()
    {
        var launched = new List<(string Path, string Args)>();
        var service = new UpdateService(
            new StubFeed(Newer()), () => new Version(1, 0, 0),
            DownloadClient(HttpStatusCode.OK, new byte[] { 1, 2, 3 }))
        {
            Launcher = (path, args) => { launched.Add((path, args)); return true; },
        };

        await service.CheckAsync(CancellationToken.None);
        var ok = await service.DownloadAndRunInstallerAsync(Newer(), CancellationToken.None);

        Assert.True(ok);
        var (path, args) = Assert.Single(launched);
        Assert.EndsWith("APMLyricsSetup-9.9.9.exe", path);
        Assert.Contains("/SILENT", args);
        Assert.Contains("/NORESTART", args);
        Assert.Contains("/SUPPRESSMSGBOXES", args);
    }

    [Fact]
    public async Task DownloadAndRun_returns_false_on_download_failure()
    {
        var service = new UpdateService(
            new StubFeed(Newer()), () => new Version(1, 0, 0),
            DownloadClient(HttpStatusCode.NotFound, Array.Empty<byte>()))
        {
            Launcher = (_, _) => true,
        };

        await service.CheckAsync(CancellationToken.None);
        var ok = await service.DownloadAndRunInstallerAsync(Newer(), CancellationToken.None);

        Assert.False(ok);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
