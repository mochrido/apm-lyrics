using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using APMLyrics.Config;
using APMLyrics.Core;

namespace APMLyrics.Ui;
public partial class OverlayWindow : Window
{
    private AppSettings _settings = AppSettings.Default;
    private LyricsDoc? _doc;
    private string _trackTitle = string.Empty;
    private string _trackArtist = string.Empty;
    private TimeSpan _position;
    private int _lastIndex = -2;
    private bool _loading;

    public OverlayWindow()
    {
        InitializeComponent();
        ApplySettings(_settings);

        // The overlay is borderless, so Windows has no non-client frame to ask
        // for resize hit-test answers. Without this the window cannot be
        // resized at all; see ResizeGrip for the measurement.
        SourceInitialized += (_, _) => ResizeGrip.Attach(this);

        // The smallest the overlay may become is a taskbar-height strip. The
        // taskbar's height is not a constant: it changes with DPI and with the
        // small-buttons setting, so it is measured from the gap between the
        // screen and the work area rather than hardcoded. Floor at 32 so a
        // surprising measurement cannot make the window unusable.
        var taskbar = SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height;
        MinHeight = Math.Max(32, Math.Min(taskbar, _settings.Height));

        // Width has no taskbar equivalent, so this is the narrowest strip that
        // still renders a few words at the fitted font floor.
        MinWidth = 160;
    }

    /// <summary>Raised when the user moves or resizes, so settings can be persisted.</summary>
    public event Action<AppSettings>? SettingsChanged;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        PrevLine.FontFamily = CurrLine.FontFamily = NextLine.FontFamily = SingleLinePanel.FontFamily = new FontFamily(settings.FontFamily);

        CurrLine.Foreground = new SolidColorBrush(ParseColor(settings.CurrentLineColor, Colors.White));
        PrevLine.Foreground = NextLine.Foreground = new SolidColorBrush(ParseColor(settings.TextColor, Colors.White));
        PrevLine.Opacity = NextLine.Opacity = settings.DimNeighbours ? settings.NeighbourOpacity : 1.0;
        SingleLinePanel.Foreground = new SolidColorBrush(ParseColor(settings.CurrentLineColor, Colors.White));

        var backdropColor = ParseColor(settings.BackdropColor, Colors.Black);
        backdropColor.A = (byte)(Math.Clamp(settings.BackdropOpacity, 0, 1) * 255);
        Backdrop.Background = new SolidColorBrush(backdropColor);

        var multi = settings.Mode == DisplayMode.MultiLine;
        MultiLinePanel.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        SingleLinePanel.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;

        // A fresh Window reports NaN for Width and Height, not 0. NaN fails every
        // comparison, so the guard must test for it explicitly or a window that
        // was never given an explicit size silently keeps none of the settings.
        if (double.IsNaN(Width) || Width <= 0) Width = settings.Width;
        if (double.IsNaN(Height) || Height <= 0) Height = settings.Height;

        ApplyFontSize();

        // Restyling is not a line change: force the text to be recomputed (a
        // neighbour-radius or display-mode edit genuinely changes what is drawn),
        // but do not replay the spec 5.2 arrival crossfade for styling. Live
        // apply makes this path run on every keystroke and slider tick, so
        // animating here would pulse the current line throughout a settings edit.
        _lastIndex = -2; // force a refresh with the new styling
        Render(animate: false);
    }

    /// <summary>
    /// Sizes the text to the current window. Resizing used to change the frame
    /// only, so shrinking the overlay clipped the text and growing it left the
    /// lyrics stranded in the middle at their old size. The configured font size
    /// is the size for the design window; everything else scales from there.
    /// </summary>
    private void ApplyFontSize()
    {
        // Rows actually drawn: the current line plus the neighbours either side,
        // capped by what the document has to offer.
        var rows = _settings.Mode == DisplayMode.MultiLine
            ? Math.Min(_settings.NeighbourRadius, _doc?.Lines.Count ?? 0) * 2 + 1
            : 1;

        var size = LineWindow.FitFontSize(_settings.FontSize, ActualWidth, ActualHeight, rows);

        CurrLine.FontSize = SingleLinePanel.FontSize = size;
        PrevLine.FontSize = NextLine.FontSize = size * 0.7;
    }

    /// <summary>
    /// Parses a colour without ever throwing. ApplySettings is public and the
    /// overlay is shared, so no settings value may kill the process; an
    /// unrenderable string falls back to the default for that element.
    /// </summary>
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
    /// Spec section 5.2: a short opacity crossfade on line change. Deliberately
    /// brief and applied only when the lyric line advances, so the MOTION 1 dial
    /// holds and the reading surface never feels animated. One animation, not a
    /// dip-and-return pair: BeginAnimation on the same property replaces the
    /// previous animation, so a second call would silently cancel the first.
    /// </summary>
    public void FadeInCurrentLine()
    {
        var fade = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.35,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
        };
        CurrLine.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Track change: clear immediately so the previous song's lines never linger.</summary>
    public void Apply(Track? track)
    {
        _doc = null;
        _loading = track is not null;
        _trackTitle = track?.Title ?? string.Empty;
        _trackArtist = Core.TrackIdentity.SplitArtist(track?.Artist ?? string.Empty);
        _lastIndex = -2;
        Render();
    }

    public void Apply(LyricsDoc? doc)
    {
        _loading = false;
        _doc = doc;
        _lastIndex = -2;
        Render();
    }

    /// <summary>Resolution finished with no match: an explicit empty state, per spec section 7.</summary>
    public void NoLyrics()
    {
        _doc = null;
        _loading = false;
        _lastIndex = -2;
        Render();
    }

    public void Tick(TimeSpan position)
    {
        _position = position;

        // The bar is gone, so a tick only matters when it moves the highlight to
        // a new line. Without this early return the overlay would still re-run
        // the matcher four times a second for nothing.
        if (_doc is not null && LineWindow.CurrentIndex(_doc.Lines, _position) == _lastIndex)
            return;

        Render();
    }

    private void Render(bool animate = true)
    {
        if (_doc is null || _doc.Lines.Count == 0)
        {
            // Three distinct states: nothing playing, loading, and no lyrics.
            // A stale line from the previous track is never one of them.
            string text;
            if (string.IsNullOrEmpty(_trackTitle))
                text = "Nothing playing";
            else if (_loading)
                text = $"{_trackTitle}\n{_trackArtist}";
            else
                text = $"{_trackTitle}\nNo lyrics for this song";

            PrevLine.Text = string.Empty;
            NextLine.Text = string.Empty;
            CurrLine.Text = text;
            SingleLinePanel.Text = text;
            return;
        }

        var index = LineWindow.CurrentIndex(_doc.Lines, _position);

        // Text changes only on a line change. This is what keeps an idle overlay
        // from repainting text 4 times a second.
        if (index != _lastIndex)
        {
            _lastIndex = index;

            var current = index >= 0 ? _doc.Lines[index].Text : string.Empty;
            CurrLine.Text = current;
            SingleLinePanel.Text = current;

            // The row count can change with the line (a shorter neighbour list at
            // the start or end of a song), so the fitted size is recomputed here
            // rather than only on resize.
            ApplyFontSize();

            // Crossfade the current line on change only. A styling-only refresh
            // (ApplySettings) passes animate: false so restyling never replays
            // this arrival animation.
            if (animate)
                FadeInCurrentLine();

            if (_settings.Mode == DisplayMode.MultiLine)
            {
                // Spec section 6 exposes the neighbour count. The overlay has one
                // TextBlock per side, so the setting is honoured by joining the
                // neighbours within the radius into that block: radius 1 gives one
                // line per side, radius 2 gives two, and so on.
                var range = LineWindow.Compute(_doc.Lines.Count, index, _settings.NeighbourRadius);

                PrevLine.Text = index > 0
                    ? string.Join("\n", Enumerable
                        .Range(range.First, Math.Max(0, index - range.First))
                        .Select(i => _doc.Lines[i].Text))
                    : string.Empty;

                var afterCurrent = index + 1;
                NextLine.Text = afterCurrent < _doc.Lines.Count
                    ? string.Join("\n", Enumerable
                        .Range(afterCurrent, Math.Min(range.Last, _doc.Lines.Count - 1) - afterCurrent + 1)
                        .Select(i => _doc.Lines[i].Text))
                    : string.Empty;
            }
        }

        // The progress fill advanced on every tick; the bar is gone, so nothing
        // here needs to run for a position-only update.
    }

    private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Raised when the button is released before the drag starts. Safe to ignore.
        }
        SettingsChanged?.Invoke(_settings with { X = Left, Y = Top });
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        // Resizing changes how much room the text has, so the size is refitted
        // here. This runs on every frame of a resize drag; it only sets font
        // sizes, which is cheap, and it is what keeps the text filling the
        // window at every size instead of staying at its old size.
        ApplyFontSize();

        SettingsChanged?.Invoke(_settings with { Width = Width, Height = Height });
    }
}
