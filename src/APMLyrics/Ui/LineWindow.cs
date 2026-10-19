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
        => FitFontSize(baseFontSize, width, height, rows, lockSize: false, lockedSize: 0);

    /// <summary>
    /// The readability floor for auto-fit: the fitted size never goes below it,
    /// because a lyric that technically renders but cannot be read is a bug.
    /// When the user locks the font size there is no floor — a deliberate
    /// choice is respected down to the 10 DIP slider minimum.
    /// </summary>
    public const double ReadabilityFloor = 14.0;

    public static double FitFontSize(
        double baseFontSize, double width, double height, int rows, bool lockSize, double lockedSize)
    {
        if (lockSize)
            return Math.Clamp(lockedSize, 10, 96);

        var usableWidth = Math.Max(0, width - PaddingX);
        var usableHeight = Math.Max(0, height - PaddingY);

        var widthScale = usableWidth / (DesignWidth - PaddingX);
        var heightScale = usableHeight / (DesignHeight - PaddingY);
        var scale = Math.Min(widthScale, heightScale);

        // Three rows (one neighbour each side) is the design layout. More rows
        // must share the same height, so shrink proportionally.
        var rowScale = rows > 3 ? 3.0 / rows : 1.0;

        return Math.Clamp(baseFontSize * scale * rowScale, ReadabilityFloor, MaxFontSize);
    }

    /// <summary>
    /// How many rows actually fit: sheds the neighbour radius one step at a
    /// time (down to zero = current line only) while the rows at the fitted
    /// size would exceed the usable height. Neighbours go before the current
    /// line ever shrinks below the readability floor.
    /// </summary>
    public static int EffectiveRows(
        int radius, int lineCount, int currentIndex, double fittedSize, double width, double height)
    {
        if (lineCount <= 0)
            return 0;

        var usableHeight = Math.Max(0, height - PaddingY);
        var current = Math.Clamp(currentIndex, 0, lineCount - 1);
        for (var r = Math.Clamp(radius, 0, 10); r >= 0; r--)
        {
            var before = Math.Min(r, current);
            var after = Math.Min(r, lineCount - 1 - current);
            if (NeededHeight(fittedSize, before + after) <= usableHeight)
                return before + after + 1;
        }
        return 1;
    }

    /// <summary>
    /// The single sizing decision the panel needs: the font size AND the row
    /// count together. For each radius from the configured one down to zero, it
    /// fits the font for THAT row count and checks whether the rows plus the
    /// spacing margins fit the height; the first radius that fits wins. This is
    /// what lets the current line regrow after the neighbours are shed, instead
    /// of staying at the many-row size.
    /// </summary>
    public static (double Size, int Rows) FitLayout(
        double baseFontSize, double width, double height, int radius, bool lockSize, double lockedSize)
    {
        if (lockSize)
            return (Math.Clamp(lockedSize, 10, 96), radius * 2 + 1);

        var usableHeight = Math.Max(0, height - PaddingY);
        for (var r = Math.Clamp(radius, 0, 10); r >= 0; r--)
        {
            var size = FitFontSize(baseFontSize, width, height, r * 2 + 1, lockSize: false, lockedSize: 0);
            if (r == 0 || NeededHeight(size, r * 2) <= usableHeight)
                return (size, r * 2 + 1);
        }
        return (ReadabilityFloor, 1);
    }

    /// <summary>
    /// Stack height for the current line plus `neighbours` neighbour rows:
    /// line height 1.3× the font size, neighbours at 70% of it, plus the 0.25×
    /// spacing margins above and below every line.
    /// </summary>
    private static double NeededHeight(double size, int neighbours)
        => neighbours * size * 0.7 * 1.55 + size * 1.55;

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
