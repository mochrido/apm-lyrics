using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace APMLyrics.Core;

public enum UpdateState
{
    Idle,
    Checking,
    Available,
    Downloading,
}

/// <summary>
/// Owns the update flow: check (once per run), notify, and on an explicit
/// user action download the setup exe and launch it silently. Never installs
/// anything on its own — the click is the user's.
/// </summary>
public sealed class UpdateService
{
    private readonly IReleaseFeed _feed;
    private readonly Func<Version> _currentVersion;
    private readonly HttpClient _downloadClient;
    private readonly object _gate = new();

    private UpdateState _state = UpdateState.Idle;
    private Task<bool>? _inFlightCheck;

    public UpdateService(IReleaseFeed feed, Func<Version> currentVersion, HttpClient? downloadClient = null)
    {
        _feed = feed;
        _currentVersion = currentVersion;
        // Production owns one download client for the app lifetime (never
        // disposed: disposing would kill a later update); a test supplies its
        // own, owned by the test.
        _downloadClient = downloadClient ?? new HttpClient();
    }

    /// <summary>Raised (possibly from a worker thread) when a newer release is found.</summary>
    public event Action<ReleaseInfo>? UpdateAvailable;

    public UpdateState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>
    /// Launcher seam: (path, arguments) → started. Swappable in tests; the
    /// real one starts the installer process.
    /// </summary>
    public Func<string, string, bool> Launcher { get; init; } = DefaultLauncher;

    /// <summary>
    /// One check per call; failures and "no update" are the same silent false.
    /// A manual check while the startup check is still in flight awaits it
    /// rather than reporting a false "up to date".
    /// </summary>
    public Task<bool> CheckAsync(CancellationToken ct)
    {
        Task<bool>? inFlight;
        lock (_gate)
        {
            if (_state == UpdateState.Checking && _inFlightCheck is not null)
                return _inFlightCheck;
            if (_state == UpdateState.Available)
                return Task.FromResult(true);
            _state = UpdateState.Checking;
            inFlight = _inFlightCheck = CheckCoreAsync(ct);
            return inFlight;
        }
    }

    private async Task<bool> CheckCoreAsync(CancellationToken ct)
    {
        try
        {
            var release = await _feed.GetLatestAsync(ct);
            var current = _currentVersion();

            if (release is null || !UpdateCheck.IsNewer(current, release.Tag))
            {
                lock (_gate)
                {
                    _state = UpdateState.Idle;
                    _inFlightCheck = null;
                }
                return false;
            }

            lock (_gate)
            {
                _state = UpdateState.Available;
                _inFlightCheck = null;
            }
            UpdateAvailable?.Invoke(release);
            return true;
        }
        catch (Exception)
        {
            lock (_gate)
            {
                _state = UpdateState.Idle;
                _inFlightCheck = null;
            }
            return false;
        }
    }

    /// <summary>
    /// Downloads the setup exe and launches it. Returns false (and leaves the
    /// app untouched) when the state is not Available — this is the
    /// double-click guard — or when the download fails, in which case the
    /// caller opens the release page in a browser instead.
    /// </summary>
    public async Task<bool> DownloadAndRunInstallerAsync(ReleaseInfo release, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_state != UpdateState.Available || release.SetupAssetUrl is null)
                return false;
            _state = UpdateState.Downloading;
        }

        try
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"APMLyricsSetup-{release.Tag.TrimStart('v', 'V')}.exe");

            using (var stream = await _downloadClient.GetStreamAsync(release.SetupAssetUrl, ct))
            using (var file = File.Create(path))
            {
                await stream.CopyToAsync(file, ct);
            }

            if (!Launcher(path, "/SILENT /NORESTART /SUPPRESSMSGBOXES"))
            {
                lock (_gate) _state = UpdateState.Available;
                return false;
            }
            return true; // the caller shuts the app down
        }
        catch (Exception)
        {
            lock (_gate) _state = UpdateState.Available;
            return false;
        }
    }

    private static bool DefaultLauncher(string path, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
