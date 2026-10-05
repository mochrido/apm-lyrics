using System.IO;
using System.Text.Json;

namespace APMLyrics.Config;

public enum DisplayMode
{
    MultiLine,
    SingleLine,
}

/// <summary>Everything the app remembers between runs.</summary>
public sealed record AppSettings
{
    public DisplayMode Mode { get; init; } = DisplayMode.MultiLine;
    public string FontFamily { get; init; } = "Segoe UI Semibold";
    public double FontSize { get; init; } = 28;
    public string TextColor { get; init; } = "#FFFFFF";
    public string CurrentLineColor { get; init; } = "#FFFFFF";
    public string BackdropColor { get; init; } = "#000000";
    public double BackdropOpacity { get; init; } = 0.55;
    public double NeighbourOpacity { get; init; } = 0.45;
    public bool DimNeighbours { get; init; } = true;
    public bool ClickThrough { get; init; }
    public double X { get; init; } = 100;
    public double Y { get; init; } = 100;
    public double Width { get; init; } = 560;
    public double Height { get; init; } = 160;

    public static AppSettings Default { get; } = new();
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "APMLyrics",
        "settings.json");

    /// <summary>Loads settings, falling back to defaults for a missing or corrupt file.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return AppSettings.Default;

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            return loaded is null ? AppSettings.Default : Sanitize(loaded);
        }
        catch (Exception)
        {
            return AppSettings.Default;
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }

    private static AppSettings Sanitize(AppSettings s) => s with
    {
        BackdropOpacity = Math.Clamp(s.BackdropOpacity, 0.0, 1.0),
        NeighbourOpacity = Math.Clamp(s.NeighbourOpacity, 0.0, 1.0),
        FontSize = Math.Clamp(s.FontSize, 10, 96),
        Width = Math.Max(240, s.Width),
        Height = Math.Max(60, s.Height),
    };
}
