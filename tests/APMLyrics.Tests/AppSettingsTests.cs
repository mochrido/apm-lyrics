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
    public void A_corrupt_colour_string_falls_back_to_the_default()
    {
        var path = TempPath();
        try
        {
            // This parses as JSON, so the corrupt-file catch in Load() never fires.
            // The three colour strings are the kind of garbage a hand-edited or
            // half-written settings.json holds, and none of them is renderable.
            File.WriteAllText(path, """
                {
                  "Mode": 1,
                  "FontSize": 42,
                  "TextColor": "zzz",
                  "CurrentLineColor": "",
                  "BackdropColor": "#FFFFFF7",
                  "Width": 640,
                  "NeighbourRadius": 2
                }
                """);

            var loaded = AppSettingsStore.Load(path);

            Assert.Equal(AppSettings.Default.TextColor, loaded.TextColor);
            Assert.Equal(AppSettings.Default.CurrentLineColor, loaded.CurrentLineColor);
            Assert.Equal(AppSettings.Default.BackdropColor, loaded.BackdropColor);

            // The rest of the file must survive: one bad colour is not a reason to
            // discard every other setting the user made.
            Assert.Equal(DisplayMode.SingleLine, loaded.Mode);
            Assert.Equal(42, loaded.FontSize);
            Assert.Equal(640, loaded.Width);
            Assert.Equal(2, loaded.NeighbourRadius);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_null_font_family_falls_back_to_the_default()
    {
        var path = TempPath();
        try
        {
            // An explicit JSON null survives deserialization (unlike an empty
            // string, a missing key, or a wrong type), and a null FontFamily
            // makes new FontFamily(...) throw in the overlay on every launch.
            // The file parses cleanly, so the corrupt-file catch in Load()
            // never fires and Sanitize is the only guard.
            File.WriteAllText(path, """{ "FontFamily": null }""");

            var loaded = AppSettingsStore.Load(path);

            Assert.Equal(AppSettings.Default.FontFamily, loaded.FontFamily);
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
