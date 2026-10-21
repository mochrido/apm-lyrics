using System.Globalization;
using System.Xml.Linq;

namespace APMLyrics.Core;

/// <summary>
/// Turns an Apple TTML lyric document into a LyricsDoc. Pure: no IO, no clock.
/// Apple writes line-level timing with per-line p elements, sometimes wrapped in verse divs.
/// <para>
/// Not every Apple document is timed. A real observed file for "Pasti Ada Jalan"
/// had <c>itunes:timing="None"</c> and no begin/end attributes at all; treating
/// the missing values as 0:00 would invent synchronization that does not exist.
/// Such a document is reported as <see cref="LyricsTiming.None"/> so the UI can
/// show a status instead of scrolling text.
/// </para>
/// </summary>
public static class TtmlParser
{
    private static readonly XNamespace Tt = "http://www.w3.org/ns/ttml";
    private static readonly XNamespace Itunes = "http://music.apple.com/lyric-ttml-internal";

    public static LyricsDoc Parse(string lyricsId, string ttml)
    {
        if (string.IsNullOrWhiteSpace(ttml))
            throw new FormatException("TTML document is empty.");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(ttml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new FormatException("TTML document is not valid XML.", ex);
        }

        var root = doc.Root ?? throw new FormatException("TTML document has no root element.");
        var body = root.Element(Tt + "body")
            ?? throw new FormatException("TTML document has no body element.");

        var lang = (string?)root.Attribute(XNamespace.Xml + "lang");
        var lines = new List<LyricLine>();
        var textParagraphs = 0;
        var timedParagraphs = 0;

        foreach (var p in body.Descendants(Tt + "p"))
        {
            var text = (p.Value ?? string.Empty).Trim();
            if (text.Length == 0)
                continue; // instrumental gap or spacer; never render an empty line

            textParagraphs++;
            var beginValue = (string?)p.Attribute("begin");
            var endValue = (string?)p.Attribute("end");
            var timed = beginValue is not null || endValue is not null;
            if (timed)
                timedParagraphs++;

            var begin = ParseTime(beginValue);
            var end = ParseTime(endValue);
            var part = (string?)p.Parent?.Attribute(Itunes + "songPart");
            lines.Add(new LyricLine(begin, end, text, part));
        }

        // One untimed paragraph among timed ones means the document's timing
        // cannot be trusted for any line: a nonempty line silently pinned to
        // 0:00 would render as a wrong highlight.
        var timing = textParagraphs == 0 || timedParagraphs != textParagraphs
            ? LyricsTiming.None
            : LyricsTiming.Line;

        if (timing == LyricsTiming.Line)
        {
            lines.Sort((a, b) => a.Begin.CompareTo(b.Begin));
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.End >= line.Begin)
                    continue;
                // A reversed/absent end is repaired to the next start, or to the
                // start itself when it is the last line. Never negative-length.
                var nextBegin = i + 1 < lines.Count ? lines[i + 1].Begin : line.Begin;
                lines[i] = line with { End = nextBegin >= line.Begin ? nextBegin : line.Begin };
            }
        }

        return new LyricsDoc(lyricsId, lang, lines) { Timing = timing };
    }

    /// <summary>Accepts both "19.265" (bare seconds) and "00:04:58.250" / "5:06.066" (clock).</summary>
    internal static TimeSpan ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.Zero;

        var s = value.Trim();
        double seconds;
        try
        {
            if (s.Contains(':'))
            {
                var parts = s.Split(':');
                if (parts.Length is < 2 or > 3)
                    throw new FormatException($"Unsupported timestamp '{value}'.");

                seconds = double.Parse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture);
                var minutes = int.Parse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture);
                var hours = parts.Length == 3
                    ? int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture)
                    : 0;
                seconds += hours * 3600.0 + minutes * 60.0;
            }
            else
            {
                seconds = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"Unsupported timestamp '{value}'.", ex);
        }

        if (!double.IsFinite(seconds) || seconds < 0)
            throw new FormatException($"Timestamp '{value}' is not a finite non-negative duration.");

        // Guard the TimeSpan conversion itself: double->ticks overflows far
        // below double.MaxValue.
        if (seconds > TimeSpan.MaxValue.TotalSeconds)
            throw new FormatException($"Timestamp '{value}' is out of range.");

        return TimeSpan.FromSeconds(seconds);
    }
}
