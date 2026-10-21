using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class TtmlParserTests
{
    private const string Doc = """
    <tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" itunes:timing="Line" xml:lang="en">
      <head><metadata><iTunesMetadata xmlns="http://music.apple.com/lyric-ttml-internal">
        <songwriters><songwriter>Example Writer</songwriter></songwriters>
      </iTunesMetadata></metadata></head>
      <body dur="00:04:58.250">
        <div begin="00:00:11.220" end="00:00:19.980">
          <p begin="00:00:11.220" end="00:00:16.450">First synthetic line</p>
          <p begin="00:00:16.450" end="00:00:19.980">Second synthetic line</p>
        </div>
      </body>
    </tt>
    """;

    [Fact]
    public void Parses_clock_form_timestamps()
    {
        var doc = TtmlParser.Parse("AP_1", Doc);
        Assert.Equal(TimeSpan.FromSeconds(11.220), doc.Lines[0].Begin);
        Assert.Equal(TimeSpan.FromSeconds(16.450), doc.Lines[0].End);
        Assert.Equal("First synthetic line", doc.Lines[0].Text);
        Assert.Equal(2, doc.Lines.Count);
    }

    [Fact]
    public void Parses_bare_second_timestamps()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body dur="5:06.066">
          <div><p begin="19.265" end="21.121">Bare seconds synthetic line</p></div>
        </body></tt>
        """;
        var doc = TtmlParser.Parse("AP_2", ttml);
        Assert.Equal(TimeSpan.FromSeconds(19.265), doc.Lines[0].Begin);
        Assert.Equal(TimeSpan.FromSeconds(21.121), doc.Lines[0].End);
    }

    [Fact]
    public void Skips_an_empty_line_left_by_an_instrumental_gap()
    {
        // Review Focus item 1: Apple emits whitespace-only p elements for
        // instrumental gaps. They must never render as blank lyrics.
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div>
            <p begin="1" end="2">before the gap</p>
            <p begin="2" end="6">   </p>
            <p begin="6" end="8">after the gap</p>
          </div>
        </body></tt>
        """;
        var doc = TtmlParser.Parse("AP_3", ttml);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal("before the gap", doc.Lines[0].Text);
        Assert.Equal("after the gap", doc.Lines[1].Text);
    }

    [Fact]
    public void Reads_language_from_the_root()
    {
        var doc = TtmlParser.Parse("AP_1", Doc);
        Assert.Equal("en", doc.Lang);
    }

    [Fact]
    public void Sorts_lines_by_begin()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div><p begin="20" end="21">second</p><p begin="10" end="11">first</p></div>
        </body></tt>
        """;
        var doc = TtmlParser.Parse("x", ttml);
        Assert.Equal("first", doc.Lines[0].Text);
        Assert.Equal("second", doc.Lines[1].Text);
    }

    [Fact]
    public void Captures_song_part_from_the_verse_div()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal"><body>
          <div itunes:songPart="Verse"><p begin="1" end="2">a</p></div>
        </body></tt>
        """;
        var doc = TtmlParser.Parse("x", ttml);
        Assert.Equal("Verse", doc.Lines[0].Part);
    }

    [Fact]
    public void Timed_document_is_reported_as_line_timed()
    {
        var doc = TtmlParser.Parse("AP_1", Doc);
        Assert.Equal(LyricsTiming.Line, doc.Timing);
    }

    [Fact]
    public void Untimed_document_is_not_claimed_as_synchronized()
    {
        // The real observed shape for Pasti Ada Jalan: itunes:timing="None",
        // paragraphs with no begin/end anywhere.
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" itunes:timing="None">
          <body>
            <div><p>static line one</p><p>static line two</p></div>
          </body>
        </tt>
        """;

        var doc = TtmlParser.Parse("GE_1", ttml);

        Assert.Equal(LyricsTiming.None, doc.Timing);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal("static line one", doc.Lines[0].Text);
    }

    [Fact]
    public void Missing_timestamps_without_a_timing_attribute_are_untimed()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div><p>no timestamps at all</p></div>
        </body></tt>
        """;

        var doc = TtmlParser.Parse("GE_2", ttml);
        Assert.Equal(LyricsTiming.None, doc.Timing);
    }

    [Fact]
    public void Mixed_timed_and_untimed_paragraphs_are_not_claimed_as_synchronized()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div>
            <p begin="1" end="2">timed</p>
            <p>untimed</p>
          </div>
        </body></tt>
        """;

        var doc = TtmlParser.Parse("AP_9", ttml);
        Assert.Equal(LyricsTiming.None, doc.Timing);
    }

    [Fact]
    public void Whitespace_spacers_do_not_make_a_timed_document_mixed()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div>
            <p begin="1" end="2">timed</p>
            <p begin="2" end="3">   </p>
            <p begin="3" end="4">also timed</p>
          </div>
        </body></tt>
        """;

        var doc = TtmlParser.Parse("AP_10", ttml);
        Assert.Equal(LyricsTiming.Line, doc.Timing);
    }

    [Fact]
    public void Non_finite_timestamp_is_a_format_failure()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div><p begin="NaN" end="2">broken</p></div>
        </body></tt>
        """;

        Assert.Throws<FormatException>(() => TtmlParser.Parse("AP_11", ttml));
    }

    [Fact]
    public void Negative_timestamp_is_a_format_failure()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div><p begin="-5" end="2">broken</p></div>
        </body></tt>
        """;

        Assert.Throws<FormatException>(() => TtmlParser.Parse("AP_12", ttml));
    }

    [Fact]
    public void Overflowing_timestamp_is_a_format_failure()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div><p begin="999999999999" end="2">broken</p></div>
        </body></tt>
        """;

        Assert.Throws<FormatException>(() => TtmlParser.Parse("AP_13", ttml));
    }

    [Fact]
    public void End_never_precedes_begin()
    {
        var ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body>
          <div><p begin="10" end="8">reversed</p></div>
        </body></tt>
        """;

        var doc = TtmlParser.Parse("AP_14", ttml);
        Assert.True(doc.Lines[0].End >= doc.Lines[0].Begin);
    }
}
