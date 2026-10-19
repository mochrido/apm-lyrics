using System.Net.Http;
using System.Text.Json;

namespace APMLyrics.Core;

/// <summary>Seam so the update check is testable without the network.</summary>
public interface IReleaseFeed
{
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct);
}

/// <summary>
/// Reads the latest release from GitHub. The repo is fixed: this app is
/// distributed from exactly one place. Every failure is a null — the update
/// path must degrade silently, because "offline" is a normal state.
/// </summary>
public sealed class GitHubReleaseFeed : IReleaseFeed
{
    public const string RepoSlug = "mochrido/apm-lyrics";

    // Shared is process-wide: it must NOT be disposed per call.
    private static readonly HttpClient Shared = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly HttpClient _client;

    public GitHubReleaseFeed(HttpClient? client = null)
    {
        // A caller-supplied client (a test seam) is owned by the caller.
        _client = client ?? Shared;
    }

    private static string AppVersion =>
        typeof(GitHubReleaseFeed).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        try
        {
            // Per-request, not on the client: a caller-supplied client (tests,
            // future callers) may not carry the header, and GitHub rejects
            // headerless requests with 403.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{RepoSlug}/releases/latest");
            request.Headers.UserAgent.ParseAdd($"APMLyrics/{AppVersion}");
            using var response = await _client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            return Parse(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Pure parse, internal for tests: any shape miss is a null.</summary>
    internal static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tag_name", out var tag) && tag.ValueKind != JsonValueKind.String)
                return null;
            if (!root.TryGetProperty("html_url", out var page) || page.ValueKind != JsonValueKind.String)
                return null;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                        asset.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String)
                    {
                        assets.Add(new ReleaseAsset(name.GetString()!, url.GetString()!));
                    }
                }
            }

            var release = new ReleaseInfo(tag.GetString() ?? string.Empty, null, page.GetString()!)
            {
                Assets = assets,
            };

            // A release with a renamed/missing setup asset is still a release:
            // surface it (SetupAssetUrl stays null) so the version comparison
            // runs and the UI can offer the browser fallback. Only a release
            // WITH the asset gets its download URL filled in.
            return UpdateCheck.PickSetupAsset(release) ?? release;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
