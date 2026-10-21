using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LrcParserTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(200);

    [Fact]
    public void Parses_plain_timestamps()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]first\n[00:20.00]second", Duration);

        Assert.Equal(LyricsTiming.Line, doc.Timing);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), doc.Lines[0].Begin);
        Assert.Equal("first", doc.Lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[1].Begin);
    }

    [Fact]
    public void Accepts_one_to_three_fractional_digits()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.2]first\n[00:20.345]second", Duration);

        Assert.Equal(TimeSpan.FromSeconds(10.2), doc.Lines[0].Begin);
        Assert.Equal(TimeSpan.FromSeconds(20.345), doc.Lines[1].Begin);
    }

    [Fact]
    public void Strips_enhanced_word_timestamps_but_keeps_the_line_timing()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.10]<00:10.10>Oh <00:10.50>no", Duration);

        Assert.Single(doc.Lines);
        Assert.Equal(TimeSpan.FromSeconds(10.1), doc.Lines[0].Begin);
        Assert.Equal("Oh no", doc.Lines[0].Text);
    }

    [Fact]
    public void Rejects_an_unparseable_timestamp()
    {
        var input = "[xx:10.00]Oh no, I see";

        var ex = Assert.Throws<FormatException>(() => LrcParser.Parse("lrclib:1", input, Duration));
        Assert.Contains("timestamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Supports_two_digit_centiseconds_only()
    {
        var doc = LrcParser.Parse("lrclib:1", "[01:02.34]only line", TimeSpan.FromSeconds(120));

        Assert.Equal(TimeSpan.FromSeconds(62.34), doc.Lines[0].Begin);
    }

    [Fact]
    public void Repeated_timestamp_shares_the_text()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00][00:20.00]Example", Duration);

        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), doc.Lines[0].Begin);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[1].Begin);
        Assert.Equal("Example", doc.Lines[0].Text);
        Assert.Equal("Example", doc.Lines[1].Text);
    }

    [Fact]
    public void Applies_a_positive_offset_earlier()
    {
        var doc = LrcParser.Parse("lrclib:1", "[offset:500]\n[00:10.00]Example", Duration);

        Assert.Equal(TimeSpan.FromSeconds(9.5), doc.Lines[0].Begin);
    }

    [Fact]
    public void Applies_a_negative_offset_later()
    {
        var doc = LrcParser.Parse("lrclib:1", "[offset:-500]\n[00:10.00]Example", Duration);

        Assert.Equal(TimeSpan.FromSeconds(10.5), doc.Lines[0].Begin);
    }

    [Fact]
    public void Clamps_an_offset_past_the_start_to_zero()
    {
        var doc = LrcParser.Parse("lrclib:1", "[offset:10500]\n[00:10.00]Example", Duration);

        Assert.Equal(TimeSpan.Zero, doc.Lines[0].Begin);
    }

    [Fact]
    public void Rejects_conflicting_offset_tags()
    {
        var input = "[offset:500]\n[offset:250]\n[00:10.00]Example";

        Assert.Throws<FormatException>(() => LrcParser.Parse("lrclib:1", input, Duration));
    }

    [Fact]
    public void Ignores_metadata_tags()
    {
        var input = """
        [ar:Example Artist]
        [ti:Example Song]
        [al:Example Album]
        [length:03:20]
        [00:10.00]first
        """;

        var doc = LrcParser.Parse("lrclib:1", input, Duration);

        Assert.Single(doc.Lines);
        Assert.Equal("first", doc.Lines[0].Text);
    }

    [Fact]
    public void Sorts_out_of_order_entries()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:20.00]second\n[00:10.00]first", Duration);

        Assert.Equal("first", doc.Lines[0].Text);
        Assert.Equal("second", doc.Lines[1].Text);
    }

    [Fact]
    public void Uses_the_next_start_as_the_end()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]first\n[00:20.00]second", Duration);

        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[0].End);
    }

    [Fact]
    public void Uses_the_track_duration_as_the_final_end()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]only", Duration);

        Assert.Equal(Duration, doc.Lines[0].End);
    }

    [Fact]
    public void Final_end_never_precedes_its_start()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]only", TimeSpan.Zero);

        Assert.True(doc.Lines[0].End >= doc.Lines[0].Begin);
    }

    [Fact]
    public void Rejects_entries_beyond_the_track_duration()
    {
        var input = "[00:10.00]first\n[00:04:00.00]far past the end";

        Assert.Throws<FormatException>(() => LrcParser.Parse("lrclib:1", input, Duration));
    }

    [Fact]
    public void Accepts_the_duration_tolerance_boundary()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]first\n[03:22.00]at the boundary", Duration);

        Assert.Equal(2, doc.Lines.Count);
    }

    [Fact]
    public void Skips_whitespace_only_entries()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]first\n[00:15.00]   \n[00:20.00]second", Duration);

        Assert.Equal(2, doc.Lines.Count);
    }

    [Fact]
    public void Rejects_timed_data_with_no_text()
    {
        Assert.Throws<FormatException>(() => LrcParser.Parse("lrclib:1", "[00:10.00]\n[00:20.00]", Duration));
    }

    [Fact]
    public void Rejects_empty_input()
    {
        Assert.Throws<FormatException>(() => LrcParser.Parse("lrclib:1", "   \n  ", Duration));
    }

    [Fact]
    public void Rejects_plain_unsynchronized_text()
    {
        Assert.Throws<FormatException>(
            () => LrcParser.Parse("lrclib:1", "just some words\nwith no timing at all", Duration));
    }

    [Fact]
    public void Unknown_duration_keeps_line_timing_and_never_invents_an_end()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]first\n[00:20.00]second", TimeSpan.Zero);

        Assert.Equal(LyricsTiming.Line, doc.Timing);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[0].End);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[1].End);
    }

    [Fact]
    public void Carries_the_lyrics_id_and_a_lang_of_null()
    {
        var doc = LrcParser.Parse("lrclib:1", "[00:10.00]first", Duration);

        Assert.Equal("lrclib:1", doc.LyricsId);
        Assert.Null(doc.Lang);
    }

    [Fact]
    public void Parses_timestamps_without_fractional_part()
    {
        var doc = LrcParser.Parse("lrclib:1", "[01:05]only line", TimeSpan.FromSeconds(120));

        Assert.Equal(TimeSpan.FromSeconds(65), doc.Lines[0].Begin);
    }
}
