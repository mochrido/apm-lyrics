using System.IO;
using APMLyrics.Config;
using Xunit;

namespace APMLyrics.Tests;

public class AppSettingsTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"apm-{Guid.NewGuid():N}.json");

    [Fact]
    public void Defaults_are_multi_line_and_not_click_through()
    {
        var d = AppSettings.Default;
        Assert.Equal(DisplayMode.MultiLine, d.Mode);
        Assert.False(d.ClickThrough);
    }

    [Fact]
    public void Round_trips_through_disk()
    {
        var path = TempPath();
        try
        {
            var original = AppSettings.Default with { X = 1234.5, Width = 640, Mode = DisplayMode.SingleLine };
            AppSettingsStore.Save(path, original);
            var loaded = AppSettingsStore.Load(path);
            Assert.Equal(1234.5, loaded.X);
            Assert.Equal(640, loaded.Width);
            Assert.Equal(DisplayMode.SingleLine, loaded.Mode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var loaded = AppSettingsStore.Load(TempPath());
        Assert.Equal(AppSettings.Default.Mode, loaded.Mode);
    }

    [Fact]
    public void Corrupt_file_returns_defaults_instead_of_throwing()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not json");
            var loaded = AppSettingsStore.Load(path);
            Assert.Equal(AppSettings.Default.Mode, loaded.Mode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Backdrop_opacity_stays_in_range()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{ "BackdropOpacity": 5.0 }""");
            var loaded = AppSettingsStore.Load(path);
            Assert.InRange(loaded.BackdropOpacity, 0.0, 1.0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Default_path_is_under_appdata()
    {
        Assert.Contains("APMLyrics", AppSettingsStore.DefaultPath);
        Assert.EndsWith("settings.json", AppSettingsStore.DefaultPath);
    }
}
