using System.Threading;
using System.Windows;
using System.Windows.Threading;
using APMLyrics.Config;
using APMLyrics.Core;
using APMLyrics.Playback;
using APMLyrics.Ui;

namespace APMLyrics;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private OverlayWindow? _overlay;
    private TrayIcon? _tray;
    private SmtcPlaybackSource? _playback;
    private AppleLyricsCache? _cache;
    private CatalogClient? _catalog;
    private LyricsResolver? _resolver;
    private LyricsCoordinator? _coordinator;
    private LrclibClient? _lrclib;
    private PlaybackClock? _clock;
    private AppSettings _settings = AppSettings.Default;
    private DispatcherTimer? _saveTimer;
    private TimeSpan _coordinatorDuration = TimeSpan.MinValue;
    private UpdateService? _updates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "APMLyricsSingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // A missing file is the normal first run: Load() falls back to
        // AppSettings.Default, so a second copy of the defaults does not belong
        // here. Two copies would drift the first time either one changed.
        _settings = AppSettingsStore.Load(AppSettingsStore.DefaultPath);

        // Settings are written on a debounce: a resize drag raises changes
        // continuously and must not write the file on every frame.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer!.Stop();
            AppSettingsStore.Save(AppSettingsStore.DefaultPath, _settings);
        };

        _overlay = new OverlayWindow
        {
            Left = _settings.X,
            Top = _settings.Y,
            Width = _settings.Width,
            Height = _settings.Height,
        };

        // Spec section 5: "a guard that pulls the window back on screen if the
        // saved monitor is gone", and "position is clamped to the virtual screen
        // bounds so the overlay cannot be dragged somewhere unrecoverable".
        // Restoring a saved position blindly strands the overlay off-screen when
        // the display layout changed, so clamp against the current virtual screen
        // before showing it. The upper bound is floored at the virtual origin so a
        // window larger than the screen still lands at the top-left, not off-screen.
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        _overlay.Left = Math.Clamp(_overlay.Left, virtualLeft, Math.Max(virtualLeft, virtualRight - _overlay.Width));
        _overlay.Top = Math.Clamp(_overlay.Top, virtualTop, Math.Max(virtualTop, virtualBottom - _overlay.Height));
        _overlay.ApplySettings(_settings);
        _overlay.SettingsChanged += OnOverlayChanged;
        _overlay.Show();

        // After Show(), so the window handle exists and the extended style can be
        // applied to it.
        _overlay.SetClickThrough(_settings.ClickThrough);

        _tray = new TrayIcon(_overlay, () => _settings, OnOverlayChanged);

        // Update check: once per start, silent on failure. A new release is
        // only ever announced; installing is the user's click.
        _updates = new UpdateService(new GitHubReleaseFeed(), CurrentVersion);
        _tray.AttachUpdates(_updates, () => Dispatcher.InvokeAsync(() => Shutdown()));
        _ = _updates.CheckAsync(CancellationToken.None);

        _cache = new AppleLyricsCache();
        _catalog = new CatalogClient();
        _resolver = new LyricsResolver(_cache, _catalog);
        _lrclib = new LrclibClient();
        _clock = new PlaybackClock(new SystemTimeSource());

        _coordinator = new LyricsCoordinator(
            (track, start) => _resolver!.ResolveCached(track, start),
            (track, start, ct) => _resolver!.ResolveLocalAsync(track, ct, start),
            (track, ct) => _lrclib!.ResolveAsync(track, ct));
        _coordinator.Changed += (generation, resolution) =>
        {
            // Raised on a worker thread: the overlay is dispatcher-affine.
            Dispatcher.InvokeAsync(() =>
            {
                // Re-check inside the dispatcher: a newer track may have been
                // adopted while this callback was queued.
                if (_playback is null || generation != _playback.Generation)
                    return;

                ApplyResolution(resolution);
            });
        };

        _cache.NewFile += _ =>
        {
            // Apple writes the lyric file mid-track: re-resolve locally so it
            // appears without waiting for the next song. This fires on the
            // watcher's thread, so hop to the UI thread before touching anything.
            Dispatcher.InvokeAsync(() => _coordinator?.NotifyCacheChanged());
        };

        _playback = new SmtcPlaybackSource();
        _playback.TrackChanged += OnTrackChanged;
        _playback.PositionChanged += OnPositionChanged;
        _playback.Start();
    }

    /// <summary>The running app's version, for the update comparison.</summary>
    private static Version CurrentVersion()
    {
        var v = typeof(App).Assembly.GetName().Version;
        return v ?? new Version(0, 0, 0);
    }

    /// <summary>
    /// Single funnel for settings changes, whether they come from a drag, a resize,
    /// the tray menu, or the settings window. Called on the UI thread.
    /// </summary>
    private void OnOverlayChanged(AppSettings settings)
    {
        _settings = settings;

        // Debounced: restart the window on every change and let it expire once the
        // gestures stop, rather than writing the file on every drag frame.
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    private void OnTrackChanged(long generation, Track? track)
    {
        // Raised from the SMTC poll loop, which runs on a thread pool thread.
        // Everything below touches WPF objects, so it must be marshalled to the
        // UI thread or it throws at runtime (WPF verifies thread affinity on
        // every access to a DispatcherObject).
        Dispatcher.InvokeAsync(() =>
        {
            // The clock adopts the new generation, so an observation belonging to
            // the previous song can never be applied to this one.
            _clock?.Reset(generation);
            _overlay?.Apply(track); // clears lines immediately, no stale lyrics

            if (track is null)
            {
                _coordinator?.Stop();
                return;
            }

            _coordinatorDuration = track.Duration;
            _coordinator?.Start(track, generation);
        });
    }

    private void OnPositionChanged(PlaybackSnapshot snapshot)
    {
        // Same thread-affinity reason as OnTrackChanged: this arrives on the poll
        // thread and writes to the overlay. The hop is cheap; it is one queued
        // operation at 4Hz, not a render loop.
        Dispatcher.InvokeAsync(() =>
        {
            _clock?.Sync(snapshot);
            if (_clock is not null)
                _overlay?.Tick(_clock.Position);

            // SMTC reports the length a moment after a track starts, and
            // occasionally reports zero mid-song. Only a real length is worth
            // acting on: a transient zero must not cancel a good resolution.
            var track = _playback?.Current;
            if (track is null
                || snapshot.Generation != _playback!.Generation
                || track.Duration <= TimeSpan.Zero
                || track.Duration == _coordinatorDuration)
                return;

            _coordinatorDuration = track.Duration;
            _coordinator?.UpdateTrack(track, snapshot.Generation);
        });
    }

    /// <summary>
    /// Renders one resolution outcome. Unsynced text is deliberately shown as a
    /// status, never as scrollable lyrics: without timestamps it cannot follow
    /// playback.
    /// </summary>
    private void ApplyResolution(LyricsResolution resolution)
    {
        if (_overlay is null)
            return;

        switch (resolution.Kind)
        {
            case LyricsResolutionKind.Synced when resolution.Document is { IsSynced: true } doc:
                // Hand the clock's current position in with the document so the
                // opening line reflects any seek that just happened.
                _overlay.Apply(doc, _clock?.Position);
                break;

            default:
                // Loading, unsynced-only, offline and miss all render as a
                // status line; untimed text must never enter the highlight.
                _overlay.Status(LyricsStatus.For(resolution));
                break;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Flush whatever the debounce has not written yet, then release the
        // resources that outlive the window: tray icon first so it cannot outlive
        // the process, then the watcher and the poll loop.
        _saveTimer?.Stop();
        AppSettingsStore.Save(AppSettingsStore.DefaultPath, _settings);
        _coordinator?.Dispose();
        _tray?.Dispose();
        _playback?.Dispose();
        _cache?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
