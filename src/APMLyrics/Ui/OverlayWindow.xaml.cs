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
    }

    /// <summary>Raised when the user moves or resizes, so settings can be persisted.</summary>
    public event Action<AppSettings>? SettingsChanged;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        PrevLine.FontFamily = CurrLine.FontFamily = NextLine.FontFamily = SingleLinePanel.FontFamily = new FontFamily(settings.FontFamily);
        PrevLine.FontSize = NextLine.FontSize = settings.FontSize * 0.7;
        CurrLine.FontSize = SingleLinePanel.FontSize = settings.FontSize;

        CurrLine.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.CurrentLineColor));
        PrevLine.Foreground = NextLine.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.TextColor));
        PrevLine.Opacity = NextLine.Opacity = settings.DimNeighbours ? settings.NeighbourOpacity : 1.0;
        SingleLinePanel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.CurrentLineColor));

        var backdropColor = (Color)ColorConverter.ConvertFromString(settings.BackdropColor);
        backdropColor.A = (byte)(Math.Clamp(settings.BackdropOpacity, 0, 1) * 255);
        Backdrop.Background = new SolidColorBrush(backdropColor);

        // The progress rule needs explicit brushes: a Rectangle with no Fill is
        // invisible, so leaving these unset would make the fill a silent no-op.
        var lineColor = (Color)ColorConverter.ConvertFromString(settings.CurrentLineColor);
        ProgressBar.Fill = new SolidColorBrush(lineColor);
        var trackColor = lineColor;
        trackColor.A = 60;
        ProgressTrack.Background = new SolidColorBrush(trackColor);

        var multi = settings.Mode == DisplayMode.MultiLine;
        MultiLinePanel.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        SingleLinePanel.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;

        // A fresh Window reports NaN for Width and Height, not 0. NaN fails every
        // comparison, so the guard must test for it explicitly or a window that
        // was never given an explicit size silently keeps none of the settings.
        if (double.IsNaN(Width) || Width <= 0) Width = settings.Width;
        if (double.IsNaN(Height) || Height <= 0) Height = settings.Height;

        _lastIndex = -2; // force a refresh with the new styling
        Render();
    }

    public void SetClickThrough(bool enabled) => ClickThrough.Apply(this, enabled);

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
        Render();
    }

    private void Render()
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
            ProgressScale.ScaleX = 0;
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

        // The progress fill advances on every tick, independently of the text
        // branch above. Setting a transform is cheap; it does not re-layout text.
        ProgressScale.ScaleX = index >= 0
            ? LineWindow.Progress(_doc.Lines[index], _position)
            : 0;
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
        SettingsChanged?.Invoke(_settings with { Width = Width, Height = Height });
    }
}
