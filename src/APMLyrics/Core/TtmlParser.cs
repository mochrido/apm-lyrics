using System.Globalization;
using System.Xml.Linq;

namespace APMLyrics.Core;

/// <summary>
/// Turns an Apple TTML lyric document into a LyricsDoc. Pure: no IO, no clock.
/// Apple writes line-level timing with per-line p elements, sometimes wrapped in verse divs.
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

        foreach (var p in body.Descendants(Tt + "p"))
        {
            var text = (p.Value ?? string.Empty).Trim();
            if (text.Length == 0)
                continue; // instrumental gap or spacer; never render an empty line

            var begin = ParseTime((string?)p.Attribute("begin"));
            var end = ParseTime((string?)p.Attribute("end"));
            var part = (string?)p.Parent?.Attribute(Itunes + "songPart");
            lines.Add(new LyricLine(begin, end, text, part));
        }

        lines.Sort((a, b) => a.Begin.CompareTo(b.Begin));
        return new LyricsDoc(lyricsId, lang, lines);
    }

    /// <summary>Accepts both "19.265" (bare seconds) and "00:04:58.250" / "5:06.066" (clock).</summary>
    internal static TimeSpan ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.Zero;

        var s = value.Trim();
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            var seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
            var minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
            var hours = parts.Length == 3 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
            return TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
        }

        return TimeSpan.FromSeconds(double.Parse(s, CultureInfo.InvariantCulture));
    }
}
