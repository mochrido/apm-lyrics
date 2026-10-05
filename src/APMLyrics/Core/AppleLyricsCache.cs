using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace APMLyrics.Core;

/// <summary>
/// Indexes Apple Music's local lyric cache and watches it for new files.
/// Apple writes into rotating bucket directories, so this rescans rather than
/// caching the directory listing forever (spec section 11, risk 1).
/// The cache root is resolved lazily on every scan rather than captured at
/// construction: if Apple Music is not running when this app starts, its cache
/// directory may not exist yet, and a root frozen at construction time would
/// leave the app showing "no lyrics" forever until restarted.
/// </summary>
public sealed class AppleLyricsCache : IDisposable, IAppleLyricsCache
{
    private FileSystemWatcher? _watcher;
    private readonly string? _explicitRoot;

    public AppleLyricsCache(string? cacheRoot = null)
    {
        _explicitRoot = cacheRoot;
        EnsureWatcher();
    }

    /// <summary>Fired when a new lyric file lands on disk mid-track.</summary>
    public event Action<Candidate>? NewFile;

    /// <summary>
    /// Creates the watcher if a cache root is now resolvable and none is attached
    /// yet. Safe to call repeatedly; it attaches at most once.
    /// </summary>
    private void EnsureWatcher()
    {
        var root = _explicitRoot ?? DefaultCacheRoot;
        if (root is null || _watcher is not null || !Directory.Exists(root))
            return;

        try
        {
            _watcher = new FileSystemWatcher(root, "ttmlLyrics*.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, e) => RaiseIfValid(e.FullPath);
            _watcher.Changed += (_, e) => RaiseIfValid(e.FullPath);
        }
        catch (Exception)
        {
            // A watcher that cannot be created is not fatal; Scan still works.
            _watcher = null;
        }
    }

    /// <summary>The package cache root, or null when Apple Music has never run.</summary>
    public static string? DefaultCacheRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packages = Path.Combine(local, "Packages");
            if (!Directory.Exists(packages))
                return null;

            foreach (var dir in Directory.EnumerateDirectories(packages, "AppleInc.AppleMusicWin_*"))
            {
                var cache = Path.Combine(dir, "AC", "INetCache");
                if (Directory.Exists(cache))
                    return cache;
            }
            return null;
        }
    }

    /// <summary>Reads every cached lyric file and returns what can be identified.</summary>
    public IReadOnlyList<Candidate> Scan()
    {
        // Re-resolve the root and attach the watcher if it has appeared since the
        // last call (Apple Music may have been launched after this app started).
        EnsureWatcher();

        var root = _explicitRoot ?? DefaultCacheRoot;
        var results = new List<Candidate>();
        if (root is null || !Directory.Exists(root))
            return results;

        foreach (var file in Directory.EnumerateFiles(root, "ttmlLyrics*.json", SearchOption.AllDirectories))
        {
            var candidate = Read(file);
            if (candidate is not null)
                results.Add(candidate);
        }
        return results;
    }

    internal static Candidate? Read(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("lyricsId", out var id) ||
                !root.TryGetProperty("ttml", out var ttml))
                return null;

            if (root.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String &&
                status.GetString() != "success")
                return null;

            var lyricsId = id.GetString() ?? string.Empty;
            var text = ttml.GetString() ?? string.Empty;
            return text.Length == 0 ? null : new Candidate(path, lyricsId, text);
        }
        catch (Exception ex)
        {
            // Spec section 7: a malformed cache file is skipped, logged at
            // debug, and the next candidate is tried. Half-written files are
            // normal here (Apple writes them mid-track), so this is routine,
            // not an error worth surfacing to the user.
            Debug.WriteLine($"Skipping malformed lyric cache file {path}: {ex.Message}");
            return null;
        }
    }

    private void RaiseIfValid(string path)
    {
        // The watcher can fire before the write completes; the retry loop in the
        // resolver handles a null read here, so a miss is safe.
        var candidate = Read(path);
        if (candidate is not null)
            NewFile?.Invoke(candidate);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
