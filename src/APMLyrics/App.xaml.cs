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
    private PlaybackClock? _clock;
    private AppSettings _settings = AppSettings.Default;
    private CancellationTokenSource? _resolveCts;
    private DispatcherTimer? _saveTimer;
    private DateTimeOffset? _trackStartedAt;

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

        _cache = new AppleLyricsCache();
        _catalog = new CatalogClient();
        _resolver = new LyricsResolver(_cache, _catalog);
        _clock = new PlaybackClock(new SystemTimeSource());

        _cache.NewFile += _ =>
        {
            // Apple writes the lyric file mid-track: re-resolve so it appears
            // without waiting for the next song. This fires on the watcher's
            // thread, so hop to the UI thread before touching anything.
            Dispatcher.InvokeAsync(() => ReResolve());
        };

        _playback = new SmtcPlaybackSource();
        _playback.TrackChanged += OnTrackChanged;
        _playback.PositionChanged += OnPositionChanged;
        _playback.Start();
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

    private void OnTrackChanged(Track? track)
    {
        // Raised from the SMTC poll loop, which runs on a thread pool thread.
        // Everything below touches WPF objects, so it must be marshalled to the
        // UI thread or it throws at runtime (WPF verifies thread affinity on
        // every access to a DispatcherObject).
        Dispatcher.InvokeAsync(() =>
        {
            _overlay?.Apply(track); // clears lines immediately, no stale lyrics
            _trackStartedAt = track is null ? null : DateTimeOffset.UtcNow;
            if (track is not null)
                ReResolve();
        });
    }

    private void OnPositionChanged(TimeSpan position, bool isPlaying, double rate)
    {
        // Same thread-affinity reason as OnTrackChanged: this arrives on the poll
        // thread and writes to the overlay. The hop is cheap; it is one queued
        // operation at 4Hz, not a render loop.
        Dispatcher.InvokeAsync(() =>
        {
            _clock?.Sync(position, isPlaying, rate);
            if (_clock is not null)
                _overlay?.Tick(_clock.Position);
        });
    }

    private async void ReResolve()
    {
        if (_resolver is null || _playback?.Current is not { } track || _overlay is null)
            return;

        // A newer track supersedes this resolution: cancel the older lookup so its
        // result cannot land on top of the newer one.
        _resolveCts?.Cancel();
        _resolveCts = new CancellationTokenSource();
        var token = _resolveCts.Token;
        var startedAt = _trackStartedAt;

        try
        {
            var doc = await _resolver.ResolveAsync(track, token, startedAt);
            if (token.IsCancellationRequested)
                return;

            // Apply/NoLyrics mutate WPF text, and the continuation after the await
            // is not guaranteed to resume on the UI thread. Awaited so the queued
            // operation is not an unobserved fire-and-forget (CS4014) in this
            // async method; the lambda still runs on the UI thread either way.
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                if (doc is null)
                    _overlay.NoLyrics();
                else
                    _overlay.Apply(doc);
            });
        }
        catch (OperationCanceledException)
        {
            // A newer track won; this resolution is obsolete.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Flush whatever the debounce has not written yet, then release the
        // resources that outlive the window: tray icon first so it cannot outlive
        // the process, then the watcher and the poll loop.
        _saveTimer?.Stop();
        AppSettingsStore.Save(AppSettingsStore.DefaultPath, _settings);
        _tray?.Dispose();
        _playback?.Dispose();
        _cache?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
