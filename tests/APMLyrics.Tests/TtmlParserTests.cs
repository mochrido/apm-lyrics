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
}
