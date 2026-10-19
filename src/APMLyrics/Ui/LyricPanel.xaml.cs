using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using APMLyrics.Config;
using APMLyrics.Core;

namespace APMLyrics.Ui;

/// <summary>
/// The lyric rendering surface, shared by the overlay and the Settings preview:
/// centered both axes, current line bright and bold, neighbours dim and smaller,
/// consistent spacing. Font sizing is the hybrid scheme from the spec: auto-fit
/// with a readability floor and neighbour shedding, or a locked manual size.
/// </summary>
public partial class LyricPanel : UserControl
{
    private AppSettings _settings = AppSettings.Default;
    private LyricsDoc? _doc;
    private string _trackTitle = string.Empty;
    private string _trackArtist = string.Empty;
    private bool _loading;
    private TimeSpan _position;
    private int _lastIndex = int.MinValue;
    private double _fittedSize;

    public LyricPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Refit();
    }

    /// <summary>The last fitted font size, exposed for diagnostics and tests.</summary>
    public double FittedFontSize => _fittedSize;

    /// <summary>Applies style settings; recomputes layout but never replays the arrival crossfade.</summary>
    public void Apply(AppSettings settings)
    {
        _settings = settings;
        _lastIndex = int.MinValue; // force a full refresh with the new styling
        Render(animate: false);
    }

    /// <summary>Content: a document to show, or one of the three empty states.</summary>
    public void Show(LyricsDoc? doc, string title, string artist, bool loading)
    {
        _doc = doc;
        _trackTitle = title;
        _trackArtist = TrackIdentity.SplitArtist(artist);
        _loading = loading;
        _lastIndex = int.MinValue;
        Render(animate: false);
    }

    /// <summary>Advances the highlight to the line covering the given position.</summary>
    public void Tick(TimeSpan position)
    {
        _position = position;
        // No document means an empty state whose text does not depend on the
        // position; skip the rebuild so an idle overlay does not churn layout
        // four times a second.
        if (_doc is null || LineWindow.CurrentIndex(_doc.Lines, _position) == _lastIndex)
            return;
        Render(animate: true);
    }

    /// <summary>Recomputes the fitted size and re-renders; called on resize and settings edits.</summary>
    public void Refit()
    {
        _lastIndex = int.MinValue;
        Render(animate: false);
    }

    private void Render(bool animate)
    {
        Lines.Children.Clear();

        if (_doc is null || _doc.Lines.Count == 0)
        {
            string text;
            if (string.IsNullOrEmpty(_trackTitle))
                text = "Nothing playing";
            else if (_loading)
                text = _trackTitle + (string.IsNullOrEmpty(_trackArtist) ? "" : "\n" + _trackArtist);
            else
                text = _trackTitle + "\nNo lyrics for this song";

            _fittedSize = FitSize(rows: 1);
            var emptySpacing = Math.Max(2, _fittedSize * 0.25);
            var empty = MakeLine(text, isCurrent: true, emptySpacing);
            empty.FontSize = _fittedSize;
            Lines.Children.Add(empty);
            _lastIndex = -1;
            return;
        }

        var index = LineWindow.CurrentIndex(_doc.Lines, _position);
        _lastIndex = index;

        var multi = _settings.Mode == DisplayMode.MultiLine;

        // The sizing decision is one call: it fits the font per candidate
        // radius and sheds neighbours when the rows do not fit, letting the
        // current line regrow in the process.
        var radius = 0;
        if (multi)
        {
            var (_, rows) = FitLayout(_settings.NeighbourRadius);
            radius = Math.Max(0, (rows - 1) / 2);
        }
        _fittedSize = FitSize(rows: radius * 2 + 1);
        var spacing = Math.Max(2, _fittedSize * 0.25);

        if (index < 0)
        {
            // Before the first line: show the first upcoming lines dimmed, in
            // order, up to the effective radius.
            var upcoming = _doc.Lines.Take(Math.Max(1, radius + 1 > _doc.Lines.Count ? _doc.Lines.Count : radius + 1));
            Lines.Children.Add(MakeLine(string.Join("\n", upcoming.Select(l => l.Text)), isCurrent: false, spacing));
            return;
        }

        if (multi)
        {
            for (var r = radius; r >= 1; r--)
            {
                var before = index - r;
                if (before >= 0)
                    Lines.Children.Add(MakeLine(_doc.Lines[before].Text, isCurrent: false, spacing));
            }
        }

        var current = MakeLine(index < _doc.Lines.Count ? _doc.Lines[index].Text : string.Empty, isCurrent: true, spacing);
        Lines.Children.Add(current);

        if (multi)
        {
            for (var r = 1; r <= radius; r++)
            {
                var after = index + r;
                if (after < _doc.Lines.Count)
                    Lines.Children.Add(MakeLine(_doc.Lines[after].Text, isCurrent: false, spacing));
            }
        }

        if (animate)
        {
            var fade = new DoubleAnimation
            {
                From = 0.35,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
            };
            current.BeginAnimation(OpacityProperty, fade);
        }
    }

    private (double Size, int Rows) FitLayout(int radius)
    {
        var width = PanelWidth;
        var height = PanelHeight;
        return LineWindow.FitLayout(
            _settings.FontSize, width, height, radius, _settings.LockFontSize, _settings.LockedFontSize);
    }

    private double PanelWidth =>
        double.IsNaN(ActualWidth) || ActualWidth <= 0 ? LineWindow.DesignWidth : ActualWidth;

    private double PanelHeight =>
        double.IsNaN(ActualHeight) || ActualHeight <= 0 ? LineWindow.DesignHeight : ActualHeight;

    private TextBlock MakeLine(string text, bool isCurrent, double spacing = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var family = new FontFamily(_settings.FontFamily);
        if (isCurrent)
        {
            block.FontFamily = family;
            block.FontWeight = FontWeights.SemiBold;
            block.Foreground = new SolidColorBrush(ParseColor(_settings.CurrentLineColor, Colors.White));
            block.FontSize = _fittedSize > 0 ? _fittedSize : _settings.FontSize;
            block.Margin = new Thickness(0, spacing, 0, spacing);
        }
        else
        {
            block.FontFamily = family;
            block.FontWeight = FontWeights.Normal;
            block.Foreground = new SolidColorBrush(ParseColor(_settings.TextColor, Colors.White));
            block.Opacity = _settings.DimNeighbours ? _settings.NeighbourOpacity : 1.0;
            block.FontSize = (_fittedSize > 0 ? _fittedSize : _settings.FontSize) * 0.7;
            block.Margin = new Thickness(0, spacing, 0, spacing);
        }
        return block;
    }

    private double FitSize(int rows)
    {
        return LineWindow.FitFontSize(
            _settings.FontSize, PanelWidth, PanelHeight, rows,
            _settings.LockFontSize, _settings.LockedFontSize);
    }

    /// <summary>Parses a colour without ever throwing; falls back to white.</summary>
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
}
