using APMLyrics.Core;

namespace APMLyrics.Ui;

/// <summary>
/// Pure windowing maths for the overlay: which lines are visible for a given
/// current index, and how far through the current line playback is.
/// </summary>
public static class LineWindow
{
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

    public static double Progress(LyricLine line, TimeSpan position)
    {
        var span = (line.End - line.Begin).TotalSeconds;
        if (span <= 0)
            return position >= line.End ? 1.0 : 0.0;

        var progress = (position - line.Begin).TotalSeconds / span;
        return Math.Clamp(progress, 0.0, 1.0);
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
