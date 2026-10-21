using System.Globalization;
using System.Text.RegularExpressions;

namespace APMLyrics.Core;

/// <summary>
/// Parses LRC (the format LRCLIB returns) into timed lines. Pure: no IO, no clock.
/// <para>
/// The parser is deliberately strict. A provider field labelled "synced" is not
/// evidence of usable timing: a shifted or unsupported timestamp would put every
/// line at the wrong moment, which is worse than showing nothing. Unusable input
/// throws <see cref="FormatException"/> so the caller can treat it as a miss.
/// </para>
/// </summary>
public static class LrcParser
{
    // [mm:ss], [mm:ss.f], [mm:ss.ff], [mm:ss.fff]
    private static readonly Regex TimeTag = new(
        @"^\[(?<m>\d{1,3}):(?<s>[0-5]?\d)(?:\.(?<f>\d{1,3}))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Enhanced LRC word timings: <00:10.10>. Stripped, since only line timing
    // is rendered.
    private static readonly Regex WordTag = new(
        @"<\d{1,3}:[0-5]?\d(?:\.\d{1,3})?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OffsetTag = new(
        @"^\[offset:(?<ms>[+-]?\d{1,9})\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // LRC's stated tolerance for how far a line may sit past the track's end
    // before the file is considered to describe a different recording.
    private static readonly TimeSpan EndTolerance = TimeSpan.FromSeconds(2);

    public static LyricsDoc Parse(string lyricsId, string lrc, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(lrc))
            throw new FormatException("LRC document is empty.");

        var offset = TimeSpan.Zero;
        var offsetSeen = false;
        var entries = new List<(TimeSpan At, string Text, int Order)>();
        var order = 0;
        var sawTimed = false;

        foreach (var rawLine in lrc.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
                continue;

            var offsetMatch = OffsetTag.Match(line);
            if (offsetMatch.Success)
            {
                var ms = long.Parse(offsetMatch.Groups["ms"].Value, CultureInfo.InvariantCulture);
                var parsed = TimeSpan.FromMilliseconds(ms);
                if (offsetSeen && parsed != offset)
                    throw new FormatException("LRC document declares conflicting offset tags.");
                offset = parsed;
                offsetSeen = true;
                continue;
            }

            if (line.StartsWith('[') && !TimeTag.IsMatch(line))
            {
                // A metadata tag ([ar:], [ti:], [length:], ...). Ignored, but a
                // line that starts with a bracket and has no timestamp anywhere
                // is not lyric text either.
                if (line.EndsWith(']'))
                    continue;
                throw new FormatException($"LRC line has an unsupported timestamp: '{Truncate(line)}'.");
            }

            var stamps = new List<TimeSpan>();
            var index = 0;
            while (index < line.Length)
            {
                var match = TimeTag.Match(line[index..]);
                if (!match.Success)
                    break;
                stamps.Add(ToTimeSpan(match));
                index += match.Length;
            }

            if (stamps.Count == 0)
                throw new FormatException($"LRC line has no timestamp: '{Truncate(line)}'.");

            sawTimed = true;
            var text = WordTag.Replace(line[index..], string.Empty).Trim();
            if (text.Length == 0)
                continue; // timed spacer: a gap, not a sung line

            foreach (var stamp in stamps)
                entries.Add((stamp, text, order++));
        }

        if (!sawTimed || entries.Count == 0)
            throw new FormatException("LRC document contains no timed lyric lines.");

        var shifted = entries
            .Select(e => (At: e.At - offset, e.Text, e.Order))
            .Select(e => (At: e.At < TimeSpan.Zero ? TimeSpan.Zero : e.At, e.Text, e.Order))
            .OrderBy(e => e.At).ThenBy(e => e.Order)
            .ToList();

        if (duration > TimeSpan.Zero)
        {
            var limit = duration + EndTolerance;
            if (shifted.Any(e => e.At > limit))
                throw new FormatException("LRC timestamps exceed the track duration.");
        }

        var lines = new List<LyricLine>(shifted.Count);
        for (var i = 0; i < shifted.Count; i++)
        {
            var (at, text, _) = shifted[i];
            var end = i + 1 < shifted.Count ? shifted[i + 1].At : duration;
            if (end < at)
                end = at; // unknown or zero duration must not create a negative line
            lines.Add(new LyricLine(at, end, text, null));
        }

        return new LyricsDoc(lyricsId, null, lines) { Timing = LyricsTiming.Line };
    }

    private static TimeSpan ToTimeSpan(Match match)
    {
        var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups["f"].Success ? match.Groups["f"].Value : "0";

        // "5" and "50" and "500" all mean fractions of a second.
        var fractionSeconds = double.Parse("0." + fraction, CultureInfo.InvariantCulture);
        var total = minutes * 60.0 + seconds + fractionSeconds;
        if (!double.IsFinite(total) || total > TimeSpan.MaxValue.TotalSeconds)
            throw new FormatException($"LRC timestamp '{match.Value}' is out of range.");
        return TimeSpan.FromSeconds(total);
    }

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
