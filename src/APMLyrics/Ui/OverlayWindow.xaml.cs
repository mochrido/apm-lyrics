using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using APMLyrics.Config;
using APMLyrics.Core;

namespace APMLyrics.Ui;

/// <summary>
/// The always-on-top lyrics window. Rendering lives in LyricPanel; this class
/// owns the chrome: position, size, drag, resize, click-through, and the
/// taskbar drop clamp.
/// </summary>
public partial class OverlayWindow : Window
{
    private AppSettings _settings = AppSettings.Default;
    private bool _suppressClamp;
    private bool _isDragMove;

    public OverlayWindow()
    {
        InitializeComponent();

        // The overlay is borderless, so Windows has no non-client frame to ask
        // for resize hit-test answers; ResizeGrip supplies them.
        SourceInitialized += (_, _) => ResizeGrip.Attach(this);

        // The smallest the overlay may become is a taskbar-height strip,
        // measured from the work area gap, floored at 32.
        var taskbar = SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height;
        MinHeight = Math.Max(32, Math.Min(taskbar, _settings.Height));
        MinWidth = 160;
    }

    /// <summary>Raised when the user moves or resizes, so settings can be persisted.</summary>
    public event Action<AppSettings>? SettingsChanged;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        // A fresh Window reports NaN, not 0; guard explicitly or a window that
        // was never given an explicit size silently keeps none of the settings.
        if (double.IsNaN(Width) || Width <= 0) Width = settings.Width;
        if (double.IsNaN(Height) || Height <= 0) Height = settings.Height;

        var backdropColor = ParseColor(settings.BackdropColor, Colors.Black);
        backdropColor.A = (byte)(Math.Clamp(settings.BackdropOpacity, 0, 1) * 255);
        Backdrop.Background = new SolidColorBrush(backdropColor);

        Panel.Apply(settings);
        _draggable = settings.Draggable;
    }

    /// <summary>Parses a colour without ever throwing; falls back to the default.</summary>
    private static Color ParseColor(string? value, Color fallback)
    {
        try
        {
            return value is not null && ColorConverter.ConvertFromString(value) is Color color
                ? color
                : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    public void SetClickThrough(bool enabled) => ClickThrough.Apply(this, enabled);

    /// <summary>
    /// Draggable OFF pins the overlay where it is: drags are ignored so an
    /// accidental grab cannot move it. Resize and click-through still work.
    /// </summary>
    public void SetDraggable(bool enabled) => _draggable = enabled;
    private bool _draggable = true;

    // The window owns the current track state so every content call (track
    // change, resolved doc, no lyrics) renders the right empty-state text.
    private string _trackTitle = string.Empty;
    private string _trackArtist = string.Empty;

    /// <summary>Track change: clear immediately so the previous song's lines never linger.</summary>
    public void Apply(Track? track)
    {
        _trackTitle = track?.Title ?? string.Empty;
        _trackArtist = track?.Artist ?? string.Empty;
        Panel.Show(null, _trackTitle, _trackArtist, track is not null);
    }

    public void Apply(LyricsDoc? doc)
    {
        Panel.Show(doc, _trackTitle, _trackArtist, loading: false);
    }

    public void NoLyrics() => Panel.Show(null, _trackTitle, _trackArtist, loading: false);

    public void Tick(TimeSpan position) => Panel.Tick(position);

    private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_draggable)
            return;

        try
        {
            // Moving the window fires LocationChanged on every mouse move; the
            // drop clamp must not run mid-drag or the position it writes cancels
            // the drag loop Windows is running. Clamp once, after the drag ends.
            _isDragMove = true;
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Raised when the button is released before the drag starts. Safe to ignore.
        }
        finally
        {
            _isDragMove = false;
        }
        ApplyDropClamp();
        SettingsChanged?.Invoke(_settings with { X = Left, Y = Top });
    }

    /// <summary>
    /// The taskbar fix: a drop that would cross into the taskbar area is lifted
    /// so the overlay sits on top of it and never gets buried. Applied after a
    /// drag and on any programmatic position change.
    /// </summary>
    private void ApplyDropClamp()
    {
        var (left, top) = DropClamp.Clamp(
            Left, Top, Width, Height,
            SystemParameters.WorkArea.Bottom,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight);

        if (Math.Abs(top - Top) > 0.01)
        {
            _suppressClamp = true;
            Top = top;
            Left = left;
            _suppressClamp = false;
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_suppressClamp || _isDragMove)
            return;
        // Clamp on every location change so nothing (snap, restore, another
        // app) can park the overlay inside the taskbar either.
        ApplyDropClamp();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        // LyricPanel refits itself on its own SizeChanged; nothing to do here
        // but persist the new geometry.
        SettingsChanged?.Invoke(_settings with { Width = Width, Height = Height });
    }
}
