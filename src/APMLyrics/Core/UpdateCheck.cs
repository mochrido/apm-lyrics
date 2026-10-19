namespace APMLyrics.Core;

/// <summary>A GitHub release and, when present, its setup asset.</summary>
public sealed record ReleaseInfo(
    string Tag,
    string? SetupAssetUrl,
    string ReleasePageUrl)
{
    public IReadOnlyList<ReleaseAsset>? Assets { get; init; }
}

public sealed record ReleaseAsset(string Name, string DownloadUrl);

/// <summary>
/// Pure decisions for the update check: tag parsing, version comparison, and
/// asset selection. No IO, so every failure mode is unit-testable.
/// </summary>
public static class UpdateCheck
{
    /// <summary>
    /// "v1.2.3" / "1.2" → Version (missing parts are zero); anything with a
    /// non-numeric component is a miss, never a crash, never a false update.
    /// </summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        if (s.Length == 0)
            return null;

        var parts = s.Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit))
                return null;
            if (!int.TryParse(parts[i], out var n))
                return null;
            numbers[i] = n;
        }
        return new Version(numbers[0],
            numbers.Length > 1 ? numbers[1] : 0,
            numbers.Length > 2 ? numbers[2] : 0);
    }

    /// <summary>True only when the tag parses AND is strictly greater.</summary>
    public static bool IsNewer(Version current, string? tag)
        => ParseTag(tag) is { } parsed && Compare(Pad(parsed), Pad(current)) > 0;

    // System.Version compares 1.0.0 as LESS than 1.0.0.0, which would report a
    // phantom update for a same-version tag written with fewer parts. Padding
    // both to four components before comparing makes equal versions equal.
    private static int[] Pad(Version v) => new[] { v.Major, v.Minor, v.Build, v.Revision };

    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < 4; i++)
        {
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        }
        return 0;
    }

    /// <summary>The release with the setup asset attached, or null when the
    /// release has no APMLyricsSetup.exe (the browser fallback applies).</summary>
    public static ReleaseInfo? PickSetupAsset(ReleaseInfo candidate)
    {
        var asset = candidate.Assets?.FirstOrDefault(
            a => a.Name == "APMLyricsSetup.exe");
        return asset is null ? null : candidate with { SetupAssetUrl = asset.DownloadUrl };
    }
}
