using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class UpdateCheckTests
{
    [Fact]
    public void ParseTag_strips_v_and_parses()
    {
        Assert.Equal(new Version(1, 2, 3), UpdateCheck.ParseTag("v1.2.3"));
        Assert.Equal(new Version(1, 2, 3), UpdateCheck.ParseTag("1.2.3"));
    }

    [Fact]
    public void ParseTag_returns_null_for_unparseable()
    {
        Assert.Null(UpdateCheck.ParseTag("v1.2-beta"));
        Assert.Null(UpdateCheck.ParseTag(""));
        Assert.Null(UpdateCheck.ParseTag(null));
        Assert.Null(UpdateCheck.ParseTag("latest"));
    }

    [Fact]
    public void ParseTag_fills_missing_parts_with_zero()
    {
        Assert.Equal(new Version(1, 2, 0), UpdateCheck.ParseTag("v1.2"));
        Assert.Equal(new Version(5, 0, 0), UpdateCheck.ParseTag("v5"));
    }

    [Fact]
    public void IsNewer_true_only_for_greater_tags()
    {
        var current = new Version(1, 0, 0);
        Assert.True(UpdateCheck.IsNewer(current, "v1.0.1"));
        Assert.False(UpdateCheck.IsNewer(current, "v1.0.0"));
        Assert.False(UpdateCheck.IsNewer(current, "v0.9"));
        Assert.False(UpdateCheck.IsNewer(current, "junk"));
        Assert.False(UpdateCheck.IsNewer(current, null));
    }

    [Fact]
    public void PickSetupAsset_selects_the_setup_exe()
    {
        var release = new ReleaseInfo(
            "v1.2.0",
            "https://example.com/other.zip",
            "https://github.com/mochrido/apm-lyrics/releases/v1.2.0")
        {
            Assets = new[]
            {
                new ReleaseAsset("checksums.txt", "https://example.com/checksums.txt"),
                new ReleaseAsset("APMLyricsSetup.exe", "https://example.com/APMLyricsSetup.exe"),
            },
        };

        var picked = UpdateCheck.PickSetupAsset(release);
        Assert.NotNull(picked);
        Assert.Equal("https://example.com/APMLyricsSetup.exe", picked.SetupAssetUrl);
    }

    [Fact]
    public void PickSetupAsset_null_when_missing()
    {
        var release = new ReleaseInfo("v1.2.0", null, "https://github.com/mochrido/apm-lyrics/releases/v1.2.0");
        Assert.Null(UpdateCheck.PickSetupAsset(release));
    }
}
