using APMLyrics.Core;

namespace APMLyrics.Ui;

/// <summary>
/// Pure windowing maths for the overlay: which lines are visible for a given
/// current index, and how large the text should be for the window it is in.
/// </summary>
public static class LineWindow
{
    /// <summary>The window size the stored font size is calibrated against.</summary>
    public const double DesignWidth = 560;
    public const double DesignHeight = 160;

    /// <summary>Border padding, as laid out in the overlay: 16 left/right, 10 top/bottom.</summary>
    private const double PaddingX = 32;
    private const double PaddingY = 20;

    /// <summary>Text smaller than this is unreadable; larger than the ceiling is a bug.</summary>
    private const double MinFontSize = 6;
    private const double MaxFontSize = 200;

    public readonly record struct Range(int First, int Current, int Last);

    public static Range Compute(int lineCount, int currentIndex, int radius)
    {
        if (lineCount <= 0)
            return new Range(0, -1, -1);

        var current = Math.Clamp(currentIndex, 0, lineCount - 1);
        var first = Math.Clamp(current - radius, 0, Math.Max(0, lineCount - (radius * 2 + 1)));
        var last = Math.Min(lineCount - 1, first + radius * 2);
        return new Range(first, current, last);
    }

    /// <summary>
    /// Font size for the current window, so the text always fills it without
    /// overflowing. The stored setting is calibrated to the design size, so a
    /// window at 560x160 renders exactly the configured size and every other
    /// size scales from there. The tighter of the two axes wins, because a
    /// short window clips vertically and a narrow one wraps into more rows.
    /// Extra neighbour rows shrink the result so five rows fit where three did.
    /// </summary>
    public static double FitFontSize(double baseFontSize, double width, double height, int rows)
    {
        var usableWidth = Math.Max(0, width - PaddingX);
        var usableHeight = Math.Max(0, height - PaddingY);

        var widthScale = usableWidth / (DesignWidth - PaddingX);
        var heightScale = usableHeight / (DesignHeight - PaddingY);
        var scale = Math.Min(widthScale, heightScale);

        // Three rows (one neighbour each side) is the design layout. More rows
        // must share the same height, so shrink proportionally.
        var rowScale = rows > 3 ? 3.0 / rows : 1.0;

        return Math.Clamp(baseFontSize * scale * rowScale, MinFontSize, MaxFontSize);
    }

    /// <summary>Index of the last line that has begun, or -1 before the first line.</summary>
    public static int CurrentIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Begin <= position)
                index = i;
            else
                break;
        }
        return index;
    }
}
