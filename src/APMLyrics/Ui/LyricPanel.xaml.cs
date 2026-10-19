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
        if (_doc is not null && LineWindow.CurrentIndex(_doc.Lines, _position) == _lastIndex)
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

            var empty = MakeLine(text, isCurrent: true);
            Lines.Children.Add(empty);
            _fittedSize = FitSize(rows: 1);
            empty.FontSize = _fittedSize;
            _lastIndex = -1;
            return;
        }

        var index = LineWindow.CurrentIndex(_doc.Lines, _position);
        _lastIndex = index;

        var multi = _settings.Mode == DisplayMode.MultiLine;
        var fitted = FitSize(rows: multi ? _settings.NeighbourRadius * 2 + 1 : 1);
        _fittedSize = fitted;

        // Shed neighbours first when the fitted size cannot fit the rows.
        var radius = 0;
        if (multi && index >= 0)
        {
            var effective = LineWindow.EffectiveRows(
                _settings.NeighbourRadius, _doc.Lines.Count, index, fitted, ActualWidth, ActualHeight);
            radius = (effective - 1) / 2;
        }

        var spacing = Math.Max(2, fitted * 0.25);

        if (index < 0)
        {
            // Before the first line: show the first line dimmed as "next".
            var upcoming = _doc.Lines[0].Text;
            if (multi && radius > 0 && _doc.Lines.Count > 1)
                Lines.Children.Add(MakeLine(upcoming + "\n" + _doc.Lines[Math.Min(1 + (radius - 1), _doc.Lines.Count - 1)].Text, isCurrent: false, spacing));
            else
                Lines.Children.Add(MakeLine(upcoming, isCurrent: false, spacing));
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
        var width = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? LineWindow.DesignWidth : ActualWidth;
        var height = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? LineWindow.DesignHeight : ActualHeight;
        return LineWindow.FitFontSize(
            _settings.FontSize, width, height, rows,
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
