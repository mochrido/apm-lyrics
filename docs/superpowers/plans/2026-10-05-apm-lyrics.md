# APM Lyrics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a persistent, draggable, resizable always-on-top lyrics overlay for Apple Music on Windows, driven by Windows SMTC playback state and Apple Music's own cached TTML lyrics.

**Architecture:** One WPF (.NET 8) process, four units behind two interfaces. `SmtcPlaybackSource` wraps the one Windows API and raises track and position events. `LyricsResolver` composes a cache index, a TTML parser, and an iTunes lookup client to turn a `Track` into a `LyricsDoc`. `OverlayWindow` renders a window of lines and owns drag, resize, and click-through. `SettingsWindow` and a tray icon are the only interactive chrome. Dependencies point inward: the parser, matcher, and clock are pure and carry the tests.

**Tech Stack:** C# / .NET 8, WPF, `net8.0-windows10.0.19041.0` target (gives WinRT projection for SMTC and `ApplicationData`), xunit for tests, WPF-UI for settings chrome, Inno Setup for the installer, GitHub Actions on `windows-latest`.

**Spec:** `docs/superpowers/specs/2026-10-05-apm-lyrics-design.md`

## Global Constraints

- Target framework: `net8.0-windows10.0.19041.0`. Minimum OS Windows 10 19041.
- Every file outside the chat is English: code, identifiers, comments, commit messages, test names, UI strings (user profile rule).
- No em dash (`—`) in any user-facing string or documentation prose (antislop R-02).
- Never commit real lyric text or real cache captures. Committed fixtures are synthetic only (spec section 8).
- Overlay default state: multi-line mode, always-on-top, not click-through.
- Settings path: `%APPDATA%\APMLyrics\settings.json`.
- No feature is added that is listed in spec section 10 (Deferred).
- Commits are conventional (`feat:`, `test:`, `chore:`, `docs:`). Commit on `main`.
- Tests: `dotnet test`. Do not mark a step done on intent; run it.

## Review Focus

Five input classes the spec implies but no task mentioned. Each is pinned to the task that owns the code, with its test written into that task's steps.

1. **Lyric line with an empty or whitespace-only body.** Apple emits `<p>` elements with no text for instrumental gaps. Expected: the line is skipped, never rendered as a blank lyric. (Task 2)
2. **TTML timestamps in clock form versus bare seconds.** Apple writes both `begin="19.265"` and `begin="00:00:11.220"` in the same cache. Expected: both parse to the same value. Getting this wrong desyncs every line. (Task 2)
3. **SMTC `Artist` carrying an album suffix.** `"Noah Kahan - The Great Divide: The Last Of The Bugs"`. Expected: matched as `Noah Kahan`, otherwise every lookup misses. (Task 4)
4. **A track whose lyrics file exists but belongs to a different song of near-identical length.** Two pairs in the reference library differ by under half a second. Expected: title and artist decide, duration only breaks a tie. (Task 4)
5. **Track change while the previous track's lyrics are on screen.** Expected: the previous lines are cleared immediately and a loading state shows; stale lyrics never linger. (Task 7)

## File Structure

```
apm-lyrics/
├─ APMLyrics.sln
├─ src/APMLyrics/
│   ├─ APMLyrics.csproj            net8.0-windows, WPF, WinRT
│   ├─ app.manifest                PerMonitorV2 DPI awareness
│   ├─ App.xaml / App.xaml.cs      startup, single instance, tray owner
│   ├─ Core/
│   │   ├─ Track.cs                record: playback snapshot
│   │   ├─ LyricsDoc.cs            records: LyricsDoc, LyricLine
│   │   ├─ TtmlParser.cs           pure: TTML text -> LyricsDoc
│   │   ├─ LyricsMatcher.cs        pure: Track + candidates -> best candidate
│   │   ├─ Candidate.cs            record: one indexed cache file
│   │   ├─ AppleLyricsCache.cs     index the cache dir, watch for new files
│   │   ├─ CatalogClient.cs        AP_ id -> TrackInfo via iTunes lookup, disk cached
│   │   └─ LyricsResolver.cs       orchestration, LyricsReady event
│   ├─ Playback/
│   │   ├─ IPlaybackSource.cs      interface
│   │   ├─ SmtcPlaybackSource.cs   WinRT SMTC wrapper
│   │   └─ PlaybackClock.cs        pure: interpolate position between SMTC ticks
│   ├─ Ui/
│   │   ├─ OverlayWindow.xaml/.cs  borderless topmost resizeable window
│   │   ├─ LineWindow.cs           pure: which lines are visible, and progress
│   │   ├─ OverlayView.xaml/.cs    renders a LineWindow
│   │   ├─ ClickThrough.cs         WS_EX_TRANSPARENT toggle helper
│   │   ├─ TrayIcon.cs             notify icon and menu
│   │   └─ SettingsWindow.xaml/.cs settings UI
│   └─ Config/
│       └─ AppSettings.cs          settings record, load/save
├─ tests/APMLyrics.Tests/
│   ├─ APMLyrics.Tests.csproj
│   ├─ TtmlParserTests.cs
│   ├─ LyricsMatcherTests.cs
│   ├─ PlaybackClockTests.cs
│   ├─ LineWindowTests.cs
│   ├─ AppSettingsTests.cs
│   ├─ CatalogClientTests.cs
│   └─ fixtures/synthetic/*.json
└─ install/APMLyrics.iss           Inno Setup script
```

`Core/TtmlParser.cs`, `Core/LyricsMatcher.cs`, `Playback/PlaybackClock.cs`, `Ui/LineWindow.cs`, and `Config/AppSettings.cs` are pure and hold the automated tests. The WinRT and WPF wrappers are thin and get a manual checklist.

---

### Task 1: Solution scaffolding and first test

**Files:**
- Create: `APMLyrics.sln`, `src/APMLyrics/APMLyrics.csproj`, `tests/APMLyrics.Tests/APMLyrics.Tests.csproj`, `tests/APMLyrics.Tests/SmokeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a building solution where `dotnet test` runs.

- [ ] **Step 1: Install the SDK (one time)**

```bash
winget install --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
```
Restart the shell, then confirm: `dotnet --list-sdks` shows an 8.x SDK.

- [ ] **Step 2: Create the projects**

```bash
cd /d/SOFTWARE/apm-lyrics
dotnet new sln -n APMLyrics
dotnet new classlib -n APMLyrics -o src/APMLyrics -f net8.0
dotnet new xunit -n APMLyrics.Tests -o tests/APMLyrics.Tests -f net8.0
dotnet sln add src/APMLyrics/APMLyrics.csproj tests/APMLyrics.Tests/APMLyrics.Tests.csproj
dotnet add tests/APMLyrics.Tests/APMLyrics.Tests.csproj reference src/APMLyrics/APMLyrics.csproj
rm src/APMLyrics/Class1.cs tests/APMLyrics.Tests/UnitTest1.cs
```

- [ ] **Step 3: Retarget the app project to Windows and WPF**

Replace `src/APMLyrics/APMLyrics.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Stays a library until Task 10 adds App.xaml, then switches to WinExe. -->
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <RootNamespace>APMLyrics</RootNamespace>
  </PropertyGroup>
</Project>
```
And `tests/APMLyrics.Tests/APMLyrics.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>APMLyrics.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\APMLyrics\APMLyrics.csproj" />
  </ItemGroup>
</Project>
```
The test project must target the same Windows TFM and enable WPF, because it references a WPF assembly.

- [ ] **Step 4: Add the DPI manifest**

Create `src/APMLyrics/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 5: Write the smoke test**

Create `tests/APMLyrics.Tests/SmokeTests.cs`:

```csharp
namespace APMLyrics.Tests;

public class SmokeTests
{
    [Fact]
    public void Solution_builds_and_runs_tests()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test`
Expected: PASS, 1 test. This also proves the WPF/WinRT TFM resolves.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "chore: scaffold solution with WPF app and xunit tests"
```

---

### Task 2: Core records and the TTML parser

**Files:**
- Create: `src/APMLyrics/Core/Track.cs`, `src/APMLyrics/Core/LyricsDoc.cs`, `src/APMLyrics/Core/TtmlParser.cs`
- Test: `tests/APMLyrics.Tests/TtmlParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record Track(string Title, string Artist, string Album, TimeSpan Duration, bool IsPlaying)`
  - `record LyricLine(TimeSpan Begin, TimeSpan End, string Text, string? Part)`
  - `record LyricsDoc(string LyricsId, string? Lang, IReadOnlyList<LyricLine> Lines)`
  - `static LyricsDoc TtmlParser.Parse(string lyricsId, string ttml)`

- [ ] **Step 1: Write the failing tests**

Create `tests/APMLyrics.Tests/TtmlParserTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter TtmlParserTests`
Expected: FAIL, `TtmlParser` and the records do not exist.

- [ ] **Step 3: Write the records**

`src/APMLyrics/Core/Track.cs`:

```csharp
namespace APMLyrics.Core;

/// <summary>A snapshot of what the player is doing right now.</summary>
public sealed record Track(
    string Title,
    string Artist,
    string Album,
    TimeSpan Duration,
    bool IsPlaying);
```

`src/APMLyrics/Core/LyricsDoc.cs`:

```csharp
namespace APMLyrics.Core;

/// <summary>One timed lyric line. Part is the optional Apple verse label.</summary>
public sealed record LyricLine(TimeSpan Begin, TimeSpan End, string Text, string? Part);

/// <summary>All timed lines for one song, as Apple cached them.</summary>
public sealed record LyricsDoc(string LyricsId, string? Lang, IReadOnlyList<LyricLine> Lines);
```

- [ ] **Step 4: Write the parser**

`src/APMLyrics/Core/TtmlParser.cs`:

```csharp
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
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter TtmlParserTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add core records and TTML parser"
```

---

### Task 3: Playback clock and line window

**Files:**
- Create: `src/APMLyrics/Playback/PlaybackClock.cs`, `src/APMLyrics/Ui/LineWindow.cs`
- Test: `tests/APMLyrics.Tests/PlaybackClockTests.cs`, `tests/APMLyrics.Tests/LineWindowTests.cs`

**Interfaces:**
- Consumes: `Core.LyricLine`, `Core.LyricsDoc`.
- Produces:
  - `class PlaybackClock` with `void Sync(TimeSpan position, bool isPlaying, double rate)`, `TimeSpan Position`, `void Pause()`, `void Resume()`.
  - `interface ITimeSource` with `TimeSpan Now { get; }` and `class SystemTimeSource : ITimeSource`.
  - `static LineWindow.Range LineWindow.Compute(int lineCount, int currentIndex, int radius)` where `Range` is `readonly record struct Range(int First, int Current, int Last)`.
  - `static double LineWindow.Progress(LyricLine line, TimeSpan position)` in `[0, 1]`.
  - `static int LineWindow.CurrentIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)`.

- [ ] **Step 1: Write the failing clock tests**

Create `tests/APMLyrics.Tests/PlaybackClockTests.cs`:

```csharp
using APMLyrics.Playback;
using Xunit;

namespace APMLyrics.Tests;

public class PlaybackClockTests
{
    private class FakeTime : ITimeSource
    {
        public TimeSpan Now { get; set; }
    }

    [Fact]
    public void Returns_the_synced_position_immediately_after_sync()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    [Fact]
    public void Interpolates_between_ticks()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromMilliseconds(500);
        Assert.Equal(TimeSpan.FromSeconds(10.5), clock.Position);
    }

    [Fact]
    public void Stops_interpolating_while_paused()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromMilliseconds(200);
        clock.Pause();
        t.Now = TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(10.2), clock.Position);
    }

    [Fact]
    public void Honours_a_non_default_rate()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.5);
        t.Now = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(13), clock.Position);
    }

    [Fact]
    public void Never_goes_backwards_when_a_coarse_tick_arrives_late()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromSeconds(1);
        _ = clock.Position; // advance to 11s
        clock.Sync(TimeSpan.FromSeconds(10.4), isPlaying: true, rate: 1.0); // late tick
        t.Now = TimeSpan.FromSeconds(1);
        Assert.True(clock.Position >= TimeSpan.FromSeconds(11));
    }
}
```

- [ ] **Step 2: Write the failing line window tests**

Create `tests/APMLyrics.Tests/LineWindowTests.cs`:

```csharp
using APMLyrics.Core;
using APMLyrics.Ui;
using Xunit;

namespace APMLyrics.Tests;

public class LineWindowTests
{
    [Fact]
    public void Centers_on_the_current_line_when_there_is_room()
    {
        var w = LineWindow.Compute(lineCount: 10, currentIndex: 5, radius: 1);
        Assert.Equal(4, w.First);
        Assert.Equal(6, w.Last);
        Assert.Equal(5, w.Current);
    }

    [Fact]
    public void Clamps_at_the_start_of_the_song()
    {
        var w = LineWindow.Compute(lineCount: 10, currentIndex: 0, radius: 1);
        Assert.Equal(0, w.First);
        Assert.Equal(2, w.Last);
    }

    [Fact]
    public void Clamps_at_the_end_of_the_song()
    {
        var w = LineWindow.Compute(lineCount: 10, currentIndex: 9, radius: 1);
        Assert.Equal(7, w.First);
        Assert.Equal(9, w.Last);
    }

    [Fact]
    public void Handles_a_song_shorter_than_the_window()
    {
        var w = LineWindow.Compute(lineCount: 1, currentIndex: 0, radius: 1);
        Assert.Equal(0, w.First);
        Assert.Equal(0, w.Last);
        Assert.Equal(0, w.Current);
    }

    [Fact]
    public void Handles_an_empty_document()
    {
        var w = LineWindow.Compute(lineCount: 0, currentIndex: -1, radius: 1);
        Assert.Equal(0, w.First);
        Assert.Equal(-1, w.Last);
        Assert.Equal(-1, w.Current);
    }

    [Fact]
    public void Progress_is_zero_at_begin_and_one_at_end()
    {
        var line = new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(4), "x", null);
        Assert.Equal(0.0, LineWindow.Progress(line, TimeSpan.Zero));
        Assert.Equal(1.0, LineWindow.Progress(line, TimeSpan.FromSeconds(4)));
        Assert.Equal(0.5, LineWindow.Progress(line, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Progress_is_clamped_outside_the_line()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "x", null);
        Assert.Equal(0.0, LineWindow.Progress(line, TimeSpan.FromSeconds(1)));
        Assert.Equal(1.0, LineWindow.Progress(line, TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void Progress_handles_a_zero_length_line()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "x", null);
        Assert.Equal(1.0, LineWindow.Progress(line, TimeSpan.FromSeconds(2)));
    }
}
```

- [ ] **Step 3: Run to verify both fail**

Run: `dotnet test --filter "PlaybackClockTests|LineWindowTests"`
Expected: FAIL, types do not exist.

- [ ] **Step 4: Write the clock**

`src/APMLyrics/Playback/PlaybackClock.cs`:

```csharp
namespace APMLyrics.Playback;

/// <summary>Injectable wall clock so the interpolation can be tested.</summary>
public interface ITimeSource
{
    TimeSpan Now { get; }
}

public sealed class SystemTimeSource : ITimeSource
{
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    public TimeSpan Now => _sw.Elapsed;
}

/// <summary>
/// SMTC position ticks are coarse. This keeps a monotonic anchor and interpolates
/// between ticks, never moving backwards, and holds still while paused.
/// </summary>
public sealed class PlaybackClock
{
    private readonly ITimeSource _time;
    private TimeSpan _anchorPosition;
    private TimeSpan _anchorAt;
    private bool _playing;
    private double _rate = 1.0;
    private TimeSpan _lastReported;

    public PlaybackClock(ITimeSource time) => _time = time;

    public void Sync(TimeSpan position, bool isPlaying, double rate)
    {
        var candidate = _playing && _rate > 0
            ? _anchorPosition + Scale(_time.Now - _anchorAt, _rate)
            : _lastReported;

        // Authoritative position wins, but only forwards. A late coarse tick
        // must never drag the displayed line backwards.
        _anchorPosition = position > candidate ? position : candidate;
        _anchorAt = _time.Now;
        _playing = isPlaying;
        _rate = rate <= 0 ? 1.0 : rate;
        _lastReported = _anchorPosition;
    }

    public TimeSpan Position
    {
        get
        {
            _lastReported = _playing
                ? _anchorPosition + Scale(_time.Now - _anchorAt, _rate)
                : _anchorPosition;
            return _lastReported;
        }
    }

    public void Pause() => _playing = false;
    public void Resume() => _playing = true;

    private static TimeSpan Scale(TimeSpan delta, double rate)
        => TimeSpan.FromTicks((long)(delta.Ticks * rate));
}
```

- [ ] **Step 5: Write the line window**

`src/APMLyrics/Ui/LineWindow.cs`:

```csharp
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
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test --filter "PlaybackClockTests|LineWindowTests"`
Expected: PASS, 13 tests.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add interpolating playback clock and line window maths"
```

---

### Task 4: Lyrics matcher

**Files:**
- Create: `src/APMLyrics/Core/Candidate.cs`, `src/APMLyrics/Core/LyricsMatcher.cs`
- Test: `tests/APMLyrics.Tests/LyricsMatcherTests.cs`

**Interfaces:**
- Consumes: `Core.Track`, `Core.LyricsDoc`.
- Produces:
  - `record Candidate(string FilePath, string LyricsId, string Ttml)` (id may be empty for later parsing).
  - `record TrackInfo(string Title, string Artist, TimeSpan Duration)`.
  - `static string TrackIdentity.SplitArtist(string smtcArtist)`.
  - `static string TrackIdentity.Normalize(string s)`.
  - `static Candidate? LyricsMatcher.Choose(Track track, IReadOnlyList<ResolvedCandidate> candidates, DateTimeOffset? trackStart = null)`.
  - `record ResolvedCandidate(Candidate Candidate, TrackInfo? Info, TimeSpan BodyDuration, DateTimeOffset WrittenAt)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/APMLyrics.Tests/LyricsMatcherTests.cs`:

```csharp
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LyricsMatcherTests
{
    private static ResolvedCandidate Cand(
        string id, string? title, string? artist, double bodySeconds,
        DateTimeOffset? written = null, string path = "c.json")
    {
        var info = title is null ? null : new TrackInfo(title, artist!, TimeSpan.FromSeconds(bodySeconds));
        return new ResolvedCandidate(
            new Candidate(path, id, "<tt/>"), info, TimeSpan.FromSeconds(bodySeconds),
            written ?? DateTimeOffset.UnixEpoch);
    }

    private static Track Track(string title, string artist, double seconds, string album = "A")
        => new(title, artist, album, TimeSpan.FromSeconds(seconds), true);

    [Fact]
    public void Splits_the_album_suffix_out_of_the_artist_field()
    {
        Assert.Equal("Noah Kahan", TrackIdentity.SplitArtist("Noah Kahan - The Great Divide: The Last Of The Bugs"));
    }

    [Fact]
    public void Leaves_a_plain_artist_untouched()
    {
        Assert.Equal("Noah Kahan", TrackIdentity.SplitArtist("Noah Kahan"));
    }

    [Fact]
    public void Normalizes_case_whitespace_and_decorations()
    {
        Assert.Equal("the great divide", TrackIdentity.Normalize("  The   Great Divide  - Single "));
    }

    [Fact]
    public void Picks_the_title_and_artist_match_even_when_durations_collide()
    {
        // Spec section 2.5: Dan 305.718 and Spoiled 306.066 are 0.35s apart.
        var candidates = new[]
        {
            Cand("AP_1872239911", "Dan", "Noah Kahan", 305.718),
            Cand("AP_1872239909", "Spoiled", "Noah Kahan", 306.066),
        };
        var track = Track("Spoiled", "Noah Kahan - The Great Divide", 306.0);

        var chosen = LyricsMatcher.Choose(track, candidates);

        Assert.NotNull(chosen);
        Assert.Equal("AP_1872239909", chosen!.LyricsId);
    }

    [Fact]
    public void Uses_duration_only_to_break_a_title_tie()
    {
        var candidates = new[]
        {
            Cand("AP_a", "Dashboard", "Noah Kahan", 230.787),
            Cand("AP_b", "Dashboard", "Noah Kahan", 231.0),
        };
        var track = Track("Dashboard", "Noah Kahan", 230.8);

        var chosen = LyricsMatcher.Choose(track, candidates);

        Assert.Equal("AP_a", chosen!.LyricsId);
    }

    [Fact]
    public void Ignores_a_title_match_with_the_wrong_artist()
    {
        var candidates = new[] { Cand("AP_a", "Dashboard", "Someone Else", 230.0) };
        var track = Track("Dashboard", "Noah Kahan", 230.0);

        Assert.Null(LyricsMatcher.Choose(track, candidates));
    }

    [Fact]
    public void Falls_back_to_a_freshly_written_unresolvable_file()
    {
        var trackStart = DateTimeOffset.Parse("2026-10-05T14:00:00Z");
        var candidates = new[]
        {
            Cand("MX_46242766-48516696", null, null!, 258.010, written: trackStart.AddSeconds(3)),
        };
        var track = Track("Some Song", "Someone", 258.0);

        var chosen = LyricsMatcher.Choose(track, candidates, trackStart);

        Assert.Equal("MX_46242766-48516696", chosen!.LyricsId);
    }

    [Fact]
    public void Rejects_a_stale_unresolvable_file()
    {
        var trackStart = DateTimeOffset.Parse("2026-10-05T14:00:00Z");
        var candidates = new[]
        {
            Cand("MX_old", null, null!, 258.0, written: trackStart.AddMinutes(-30)),
        };
        var track = Track("Some Song", "Someone", 258.0);

        Assert.Null(LyricsMatcher.Choose(track, candidates, trackStart));
    }

    [Fact]
    public void Returns_null_when_nothing_matches_so_stale_lyrics_are_never_reused()
    {
        var candidates = new[] { Cand("AP_a", "Other Song", "Noah Kahan", 100.0) };
        var track = Track("Dashboard", "Noah Kahan", 230.0);

        Assert.Null(LyricsMatcher.Choose(track, candidates));
    }

    [Fact]
    public void Tolerates_a_small_duration_difference_between_smtc_and_ttml()
    {
        var candidates = new[] { Cand("AP_a", "Dashboard", "Noah Kahan", 230.787) };
        var track = Track("Dashboard", "Noah Kahan", 232.5);

        Assert.NotNull(LyricsMatcher.Choose(track, candidates));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter LyricsMatcherTests`
Expected: FAIL, types do not exist.

- [ ] **Step 3: Write the candidates and matcher**

`src/APMLyrics/Core/Candidate.cs`:

```csharp
namespace APMLyrics.Core;

/// <summary>One indexed cache file, read from disk but not yet parsed.</summary>
public sealed record Candidate(string FilePath, string LyricsId, string Ttml);

/// <summary>What the public catalog says a lyrics id actually is.</summary>
public sealed record TrackInfo(string Title, string Artist, TimeSpan Duration);

/// <summary>A candidate plus everything known about it, ready to be chosen between.</summary>
public sealed record ResolvedCandidate(
    Candidate Candidate,
    TrackInfo? Info,
    TimeSpan BodyDuration,
    DateTimeOffset WrittenAt);
```

`src/APMLyrics/Core/LyricsMatcher.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace APMLyrics.Core;

/// <summary>String rules that make SMTC names and catalog names comparable.</summary>
public static class TrackIdentity
{
    /// <summary>Apple packs the album into the artist field: "Artist - Album[ - Single]".</summary>
    public static string SplitArtist(string smtcArtist)
    {
        if (string.IsNullOrEmpty(smtcArtist))
            return string.Empty;

        var dash = smtcArtist.IndexOf(" - ", StringComparison.Ordinal);
        return dash < 0 ? smtcArtist.Trim() : smtcArtist[..dash].Trim();
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        var lowered = s.Trim().ToLowerInvariant();
        foreach (var suffix in new[] { " - single", " - ep", " - live", " (single)", " (ep)" })
        {
            if (lowered.EndsWith(suffix, StringComparison.Ordinal))
                lowered = lowered[..^suffix.Length].Trim();
        }

        var stripped = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(stripped.Length);
        var lastWasSpace = false;
        foreach (var ch in stripped)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // accent-insensitive compare
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}

/// <summary>
/// Decides which cached lyric file belongs to the playing track.
/// Title and artist are the key; duration only breaks ties, because two songs
/// on one album can sit under half a second apart (spec section 2.5).
/// </summary>
public static class LyricsMatcher
{
    public static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ArrivalWindow = TimeSpan.FromSeconds(15);

    public static Candidate? Choose(
        Track track, IReadOnlyList<ResolvedCandidate> candidates, DateTimeOffset? trackStart = null)
    {
        var title = TrackIdentity.Normalize(track.Title);
        var artist = TrackIdentity.Normalize(TrackIdentity.SplitArtist(track.Artist));

        var exact = candidates
            .Where(c => c.Info is not null
                        && TrackIdentity.Normalize(c.Info.Title) == title
                        && TrackIdentity.Normalize(c.Info.Artist) == artist)
            .OrderBy(c => Math.Abs((c.BodyDuration - track.Duration).TotalSeconds))
            .FirstOrDefault();

        if (exact is not null && WithinTolerance(exact, track))
            return exact.Candidate;

        // Second tier: a file we cannot resolve by name, accepted only when it
        // appeared as this track started and its length agrees with SMTC.
        if (trackStart is not null)
        {
            var fresh = candidates
                .Where(c => c.Info is null)
                .Where(c => Math.Abs((c.WrittenAt - trackStart.Value).TotalSeconds) <= ArrivalWindow.TotalSeconds)
                .Where(c => Math.Abs((c.BodyDuration - track.Duration).TotalSeconds) <= DurationTolerance.TotalSeconds)
                .OrderBy(c => Math.Abs((c.WrittenAt - trackStart.Value).TotalSeconds))
                .FirstOrDefault();

            if (fresh is not null)
                return fresh.Candidate;
        }

        return null;
    }

    private static bool WithinTolerance(ResolvedCandidate candidate, Track track)
        => Math.Abs((candidate.BodyDuration - track.Duration).TotalSeconds) <= DurationTolerance.TotalSeconds;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter LyricsMatcherTests`
Expected: PASS, 10 tests. The collision tests encode spec section 2.5.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add lyrics matcher keyed on title and artist with duration tiebreak"
```

---

### Task 5: Settings model

**Files:**
- Create: `src/APMLyrics/Config/AppSettings.cs`
- Test: `tests/APMLyrics.Tests/AppSettingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record AppSettings` with `DisplayMode Mode`, `double FontSize`, `string FontFamily`, `string TextColor`, `string CurrentLineColor`, `string BackdropColor`, `double BackdropOpacity`, `double NeighbourOpacity`, `bool DimNeighbours`, `bool ClickThrough`, `double X`, `double Y`, `double Width`, `double Height`.
  - `enum DisplayMode { MultiLine, SingleLine }`
  - `static AppSettings AppSettings.Default`
  - `static AppSettings AppSettingsStore.Load(string path)`, `void AppSettingsStore.Save(string path, AppSettings settings)`
  - `static string AppSettingsStore.DefaultPath`

- [ ] **Step 1: Write the failing tests**

Create `tests/APMLyrics.Tests/AppSettingsTests.cs`:

```csharp
using APMLyrics.Config;
using Xunit;

namespace APMLyrics.Tests;

public class AppSettingsTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"apm-{Guid.NewGuid():N}.json");

    [Fact]
    public void Defaults_are_multi_line_and_not_click_through()
    {
        var d = AppSettings.Default;
        Assert.Equal(DisplayMode.MultiLine, d.Mode);
        Assert.False(d.ClickThrough);
    }

    [Fact]
    public void Round_trips_through_disk()
    {
        var path = TempPath();
        try
        {
            var original = AppSettings.Default with { X = 1234.5, Width = 640, Mode = DisplayMode.SingleLine };
            AppSettingsStore.Save(path, original);
            var loaded = AppSettingsStore.Load(path);
            Assert.Equal(1234.5, loaded.X);
            Assert.Equal(640, loaded.Width);
            Assert.Equal(DisplayMode.SingleLine, loaded.Mode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var loaded = AppSettingsStore.Load(TempPath());
        Assert.Equal(AppSettings.Default.Mode, loaded.Mode);
    }

    [Fact]
    public void Corrupt_file_returns_defaults_instead_of_throwing()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not json");
            var loaded = AppSettingsStore.Load(path);
            Assert.Equal(AppSettings.Default.Mode, loaded.Mode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Backdrop_opacity_stays_in_range()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{ "BackdropOpacity": 5.0 }""");
            var loaded = AppSettingsStore.Load(path);
            Assert.InRange(loaded.BackdropOpacity, 0.0, 1.0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Default_path_is_under_appdata()
    {
        Assert.Contains("APMLyrics", AppSettingsStore.DefaultPath);
        Assert.EndsWith("settings.json", AppSettingsStore.DefaultPath);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter AppSettingsTests`
Expected: FAIL, `AppSettings` does not exist.

- [ ] **Step 3: Write the settings**

`src/APMLyrics/Config/AppSettings.cs`:

```csharp
using System.Text.Json;

namespace APMLyrics.Config;

public enum DisplayMode
{
    MultiLine,
    SingleLine,
}

/// <summary>Everything the app remembers between runs.</summary>
public sealed record AppSettings
{
    public DisplayMode Mode { get; init; } = DisplayMode.MultiLine;
    public string FontFamily { get; init; } = "Segoe UI Semibold";
    public double FontSize { get; init; } = 28;
    public string TextColor { get; init; } = "#FFFFFF";
    public string CurrentLineColor { get; init; } = "#FFFFFF";
    public string BackdropColor { get; init; } = "#000000";
    public double BackdropOpacity { get; init; } = 0.55;
    public double NeighbourOpacity { get; init; } = 0.45;
    public bool DimNeighbours { get; init; } = true;
    public bool ClickThrough { get; init; }
    public double X { get; init; } = 100;
    public double Y { get; init; } = 100;
    public double Width { get; init; } = 560;
    public double Height { get; init; } = 160;

    public static AppSettings Default { get; } = new();
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "APMLyrics",
        "settings.json");

    /// <summary>Loads settings, falling back to defaults for a missing or corrupt file.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return AppSettings.Default;

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            return loaded is null ? AppSettings.Default : Sanitize(loaded);
        }
        catch (Exception)
        {
            return AppSettings.Default;
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }

    private static AppSettings Sanitize(AppSettings s) => s with
    {
        BackdropOpacity = Math.Clamp(s.BackdropOpacity, 0.0, 1.0),
        NeighbourOpacity = Math.Clamp(s.NeighbourOpacity, 0.0, 1.0),
        FontSize = Math.Clamp(s.FontSize, 10, 96),
        Width = Math.Max(240, s.Width),
        Height = Math.Max(60, s.Height),
    };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter AppSettingsTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add settings model with tolerant load and save"
```

---

### Task 6: Lyrics cache index and catalog client

**Files:**
- Create: `src/APMLyrics/Core/AppleLyricsCache.cs`, `src/APMLyrics/Core/CatalogClient.cs`
- Test: `tests/APMLyrics.Tests/CatalogClientTests.cs`, `tests/APMLyrics.Tests/fixtures/synthetic/catalog-lookup.json`

**Interfaces:**
- Consumes: `Core.Candidate`, `Core.TrackInfo`.
- Produces:
  - `interface IAppleLyricsCache` with `IReadOnlyList<Candidate> Scan()` (declared here; Task 7's resolver consumes it).
  - `interface ICatalogClient` with `Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct)` (declared here; Task 7's resolver consumes it).
  - `class AppleLyricsCache : IDisposable, IAppleLyricsCache` with `AppleLyricsCache(string? cacheRoot = null)`, `IReadOnlyList<Candidate> Scan()`, `event Action<Candidate> NewFile`, `static string? DefaultCacheRoot`.
  - `class CatalogClient : ICatalogClient` with `CatalogClient(HttpMessageHandler? handler = null, string? cachePath = null)`, `Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct)`.

- [ ] **Step 1: Write the failing catalog tests**

Create `tests/APMLyrics.Tests/fixtures/synthetic/catalog-lookup.json`:

```json
{
  "resultCount": 1,
  "results": [
    {
      "wrapperType": "track",
      "kind": "song",
      "trackId": 1872239909,
      "trackName": "Spoiled",
      "artistName": "Noah Kahan",
      "collectionName": "The Great Divide",
      "trackTimeMillis": 306066
    }
  ]
}
```

Create `tests/APMLyrics.Tests/CatalogClientTests.cs`:

```csharp
using System.Net;
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class CatalogClientTests
{
    private class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public int Calls { get; private set; }
        public StubHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body),
            });
        }
    }

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic", "catalog-lookup.json");

    private static string Body => File.ReadAllText(FixturePath);

    [Theory]
    [InlineData("AP_1872239909", "1872239909")]
    [InlineData("MX_46242766-48516696", null)]
    [InlineData("", null)]
    [InlineData("AP_", null)]
    public void Extracts_a_song_id_only_from_the_ap_form(string lyricsId, string? expected)
    {
        Assert.Equal(expected, CatalogClient.SongIdFromLyricsId(lyricsId));
    }

    [Fact]
    public async Task Parses_a_lookup_response()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var client = new CatalogClient(new StubHandler(Body), cache);
            var info = await client.LookupAsync("AP_1872239909", CancellationToken.None);

            Assert.NotNull(info);
            Assert.Equal("Spoiled", info!.Title);
            Assert.Equal("Noah Kahan", info.Artist);
            Assert.Equal(306.066, info.Duration.TotalSeconds, 3);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_second_lookup_is_served_from_disk_without_a_request()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var handler = new StubHandler(Body);
            var client = new CatalogClient(handler, cache);

            await client.LookupAsync("AP_1872239909", CancellationToken.None);
            var second = await client.LookupAsync("AP_1872239909", CancellationToken.None);

            Assert.Equal(1, handler.Calls); // warm cache: offline after first lookup
            Assert.Equal("Spoiled", second!.Title);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_network_failure_returns_null_and_does_not_throw()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var client = new CatalogClient(new ThrowingHandler(), cache);
            var info = await client.LookupAsync("AP_1872239909", CancellationToken.None);
            Assert.Null(info);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task A_network_failure_still_serves_a_warm_entry()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var warm = new CatalogClient(new StubHandler(Body), cache);
            await warm.LookupAsync("AP_1872239909", CancellationToken.None);

            var cold = new CatalogClient(new ThrowingHandler(), cache);
            var info = await cold.LookupAsync("AP_1872239909", CancellationToken.None);

            Assert.Equal("Spoiled", info!.Title);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task An_empty_result_set_returns_null()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"apm-cat-{Guid.NewGuid():N}.json");
        try
        {
            var client = new CatalogClient(new StubHandler("""{"resultCount":0,"results":[]}"""), cache);
            Assert.Null(await client.LookupAsync("AP_999", CancellationToken.None));
        }
        finally { File.Delete(cache); }
    }

    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter CatalogClientTests`
Expected: FAIL, `CatalogClient` does not exist.

- [ ] **Step 3: Write the catalog client**

`src/APMLyrics/Core/CatalogClient.cs`:

```csharp
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APMLyrics.Core;

/// <summary>Seam so the resolver can be tested without touching the real cache directory.</summary>
public interface IAppleLyricsCache
{
    IReadOnlyList<Candidate> Scan();
}

/// <summary>Seam so the resolver can be tested without a network.</summary>
public interface ICatalogClient
{
    Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct);
}

/// <summary>
/// Resolves an AP_ lyrics id to a real track through the public iTunes lookup.
/// Every result is cached to disk, so after the first warm-up the app works offline.
/// A network failure is never fatal: it returns null and the caller degrades.
/// </summary>
public sealed class CatalogClient : ICatalogClient
{
    private static readonly HttpClient Shared = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly HttpMessageHandler? _handler;
    private readonly string _cachePath;
    private readonly Dictionary<string, TrackInfo?> _memory = new();
    private readonly object _gate = new();

    public CatalogClient(HttpMessageHandler? handler = null, string? cachePath = null)
    {
        _handler = handler;
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "APMLyrics", "catalog-cache.json");
        LoadDiskCache();
    }

    /// <summary>"AP_1872239909" to "1872239909"; anything else (MX_, empty) to null.</summary>
    public static string? SongIdFromLyricsId(string lyricsId)
    {
        if (string.IsNullOrEmpty(lyricsId) || !lyricsId.StartsWith("AP_", StringComparison.Ordinal))
            return null;

        var id = lyricsId[3..];
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }

    public async Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_memory.TryGetValue(lyricsId, out var cached))
                return cached;
        }

        var songId = SongIdFromLyricsId(lyricsId);
        if (songId is null)
            return null;

        TrackInfo? info = null;
        try
        {
            using var client = _handler is null ? Shared : new HttpClient(_handler);
            var url = $"https://itunes.apple.com/lookup?id={songId}";
            var json = await client.GetStringAsync(url, ct);
            info = Parse(json);
        }
        catch (Exception)
        {
            info = null; // offline or malformed: degrade, do not throw
        }

        lock (_gate)
        {
            _memory[lyricsId] = info;
        }
        SaveDiskCache();
        return info;
    }

    internal static TrackInfo? Parse(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<LookupResponse>(json);
            var track = doc?.Results?.FirstOrDefault();
            if (track?.TrackName is null || track.ArtistName is null)
                return null;

            var seconds = (track.TrackTimeMillis ?? 0) / 1000.0;
            return new TrackInfo(track.TrackName, track.ArtistName, TimeSpan.FromSeconds(seconds));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void LoadDiskCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return;

            var json = File.ReadAllText(_cachePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedTrack>>(json);
            if (loaded is null)
                return;

            foreach (var (key, value) in loaded)
            {
                _memory[key] = value.Title is null
                    ? null
                    : new TrackInfo(value.Title, value.Artist ?? string.Empty, TimeSpan.FromSeconds(value.Seconds));
            }
        }
        catch (Exception)
        {
            // A corrupt cache is not worth failing over; it refills on next lookup.
        }
    }

    private void SaveDiskCache()
    {
        try
        {
            Dictionary<string, CachedTrack> snapshot;
            lock (_gate)
            {
                snapshot = _memory.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value is null
                        ? new CachedTrack(null, null, 0)
                        : new CachedTrack(kv.Value.Title, kv.Value.Artist, kv.Value.Duration.TotalSeconds));
            }

            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }

    private sealed record CachedTrack(string? Title, string? Artist, double Seconds);

    private sealed record LookupResponse(
        [property: JsonPropertyName("resultCount")] int ResultCount,
        [property: JsonPropertyName("results")] List<LookupTrack>? Results);

    private sealed record LookupTrack(
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("trackTimeMillis")] long? TrackTimeMillis);
}
```

- [ ] **Step 4: Write the cache index**

`src/APMLyrics/Core/AppleLyricsCache.cs`:

```csharp
using System.Text.Json;

namespace APMLyrics.Core;

/// <summary>
/// Indexes Apple Music's local lyric cache and watches it for new files.
/// Apple writes into rotating bucket directories, so this rescans rather than
/// caching the directory listing forever (spec section 11, risk 1).
/// </summary>
public sealed class AppleLyricsCache : IDisposable, IAppleLyricsCache
{
    private readonly FileSystemWatcher? _watcher;
    private readonly string? _root;

    public AppleLyricsCache(string? cacheRoot = null)
    {
        _root = cacheRoot ?? DefaultCacheRoot;

        if (_root is not null && Directory.Exists(_root))
        {
            _watcher = new FileSystemWatcher(_root, "ttmlLyrics*.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, e) => RaiseIfValid(e.FullPath);
            _watcher.Changed += (_, e) => RaiseIfValid(e.FullPath);
        }
    }

    /// <summary>Fired when a new lyric file lands on disk mid-track.</summary>
    public event Action<Candidate>? NewFile;

    /// <summary>The package cache root, or null when Apple Music has never run.</summary>
    public static string? DefaultCacheRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packages = Path.Combine(local, "Packages");
            if (!Directory.Exists(packages))
                return null;

            foreach (var dir in Directory.EnumerateDirectories(packages, "AppleInc.AppleMusicWin_*"))
            {
                var cache = Path.Combine(dir, "AC", "INetCache");
                if (Directory.Exists(cache))
                    return cache;
            }
            return null;
        }
    }

    /// <summary>Reads every cached lyric file and returns what can be identified.</summary>
    public IReadOnlyList<Candidate> Scan()
    {
        var results = new List<Candidate>();
        if (_root is null || !Directory.Exists(_root))
            return results;

        foreach (var file in Directory.EnumerateFiles(_root, "ttmlLyrics*.json", SearchOption.AllDirectories))
        {
            var candidate = Read(file);
            if (candidate is not null)
                results.Add(candidate);
        }
        return results;
    }

    internal static Candidate? Read(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("lyricsId", out var id) ||
                !root.TryGetProperty("ttml", out var ttml))
                return null;

            if (root.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String &&
                status.GetString() != "success")
                return null;

            var lyricsId = id.GetString() ?? string.Empty;
            var text = ttml.GetString() ?? string.Empty;
            return text.Length == 0 ? null : new Candidate(path, lyricsId, text);
        }
        catch (Exception)
        {
            return null; // a half-written file is skipped, not fatal
        }
    }

    private void RaiseIfValid(string path)
    {
        // The watcher can fire before the write completes; the retry loop in the
        // resolver handles a null read here, so a miss is safe.
        var candidate = Read(path);
        if (candidate is not null)
            NewFile?.Invoke(candidate);
    }

    public void Dispose() => _watcher?.Dispose();
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter CatalogClientTests`
Expected: PASS, 9 tests (the Theory contributes 4).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add lyrics cache index and offline-tolerant catalog client"
```

---

### Task 7: SMTC playback source and lyrics resolver

**Files:**
- Create: `src/APMLyrics/Playback/IPlaybackSource.cs`, `src/APMLyrics/Playback/SmtcPlaybackSource.cs`, `src/APMLyrics/Core/LyricsResolver.cs`

**Interfaces:**
- Consumes: `Core.Track`, `Core.Candidate`, `Core.LyricsMatcher`, `Core.TtmlParser`, `Core.CatalogClient`, `Core.AppleLyricsCache`, `Core.IAppleLyricsCache`, `Core.ICatalogClient` (both interfaces declared in Task 6).
- Produces:
  - `interface IPlaybackSource` with `event Action<Track?> TrackChanged`, `event Action<TimeSpan, bool, double> PositionChanged`, `Track? Current { get; }`, `void Start()`, `void Dispose()`.
  - `class SmtcPlaybackSource : IPlaybackSource, IDisposable` (polls SMTC at 4Hz).
  - `class LyricsResolver` with `LyricsResolver(IAppleLyricsCache cache, ICatalogClient catalog)`, `Task<LyricsDoc?> ResolveAsync(Track track, CancellationToken ct, DateTimeOffset? trackStart = null)`.
  - `static TimeSpan LyricsResolver.BodyDuration(string ttml)` (internal, used by the resolver).

**Note:** This task straddles a testable seam (`LyricsResolver` can be tested with a fake cache) and a thin WinRT wrapper (`SmtcPlaybackSource` is verified by the manual checklist, since it depends on a live Apple Music session). Split as such.

- [ ] **Step 1: Write the failing resolver test**

Create `tests/APMLyrics.Tests/LyricsResolverTests.cs`:

```csharp
using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LyricsResolverTests
{
    private const string DanTtml = """
    <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="5:05.718">
      <div><p begin="1" end="2">dan line one</p></div>
    </body></tt>
    """;

    private const string SpoiledTtml = """
    <tt xmlns="http://www.w3.org/ns/ttml" xml:lang="en"><body dur="5:06.066">
      <div><p begin="1" end="2">spoiled line one</p></div>
    </body></tt>
    """;

    /// <summary>Cache and catalog stubbed so resolution logic is tested without disk or network.</summary>
    private sealed class FakeCache : IAppleLyricsCache
    {
        public List<Candidate> Candidates { get; } = new();
        public IReadOnlyList<Candidate> Scan() => Candidates;
    }

    private sealed class FakeCatalog : ICatalogClient
    {
        public Dictionary<string, TrackInfo?> Answers { get; } = new();
        public Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct)
            => Task.FromResult(Answers.TryGetValue(lyricsId, out var v) ? v : null);
    }

    [Fact]
    public async Task Resolves_by_title_and_artist_when_two_songs_have_near_identical_durations()
    {
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("dan.json", "AP_1872239911", DanTtml));
        cache.Candidates.Add(new Candidate("spoiled.json", "AP_1872239909", SpoiledTtml));

        var catalog = new FakeCatalog();
        catalog.Answers["AP_1872239911"] = new TrackInfo("Dan", "Noah Kahan", TimeSpan.FromSeconds(305.718));
        catalog.Answers["AP_1872239909"] = new TrackInfo("Spoiled", "Noah Kahan", TimeSpan.FromSeconds(306.066));

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Spoiled", "Noah Kahan - The Great Divide", "The Great Divide", TimeSpan.FromSeconds(306.0), true);

        var doc = await resolver.ResolveAsync(track, CancellationToken.None);

        Assert.NotNull(doc);
        Assert.Equal("AP_1872239909", doc!.LyricsId);
        Assert.Equal("spoiled line one", doc.Lines[0].Text);
    }

    [Fact]
    public async Task Returns_null_when_nothing_matches()
    {
        var cache = new FakeCache();
        cache.Candidates.Add(new Candidate("dan.json", "AP_1872239911", DanTtml));
        var catalog = new FakeCatalog();
        catalog.Answers["AP_1872239911"] = new TrackInfo("Dan", "Noah Kahan", TimeSpan.FromSeconds(305.718));

        var resolver = new LyricsResolver(cache, catalog);
        var track = new Track("Unrelated", "Nobody", "X", TimeSpan.FromSeconds(200), true);

        Assert.Null(await resolver.ResolveAsync(track, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter LyricsResolverTests`
Expected: FAIL, `LyricsResolver` and `IAppleLyricsCache` do not exist.

- [ ] **Step 3: Write the interfaces and resolver**

`src/APMLyrics/Core/LyricsResolver.cs`:

```csharp
namespace APMLyrics.Core;

/// <summary>
/// Turns a playing track into lyrics: scan the cache, identify each candidate,
/// let the matcher choose, then parse the winner. Never returns stale lyrics:
/// no match means null, and the caller shows its empty state.
/// </summary>
public sealed class LyricsResolver
{
    private readonly IAppleLyricsCache _cache;
    private readonly ICatalogClient _catalog;

    public LyricsResolver(IAppleLyricsCache cache, ICatalogClient catalog)
    {
        _cache = cache;
        _catalog = catalog;
    }

    public async Task<LyricsDoc?> ResolveAsync(Track track, CancellationToken ct, DateTimeOffset? trackStart = null)
    {
        var candidates = _cache.Scan();
        if (candidates.Count == 0)
            return null;

        var resolved = new List<ResolvedCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var info = await _catalog.LookupAsync(candidate.LyricsId, ct);
            var body = BodyDuration(candidate.Ttml);
            resolved.Add(new ResolvedCandidate(candidate, info, body, WrittenAt(candidate.FilePath)));
        }

        var chosen = LyricsMatcher.Choose(track, resolved, trackStart);
        if (chosen is null)
            return null;

        try
        {
            return TtmlParser.Parse(chosen.LyricsId, chosen.Ttml);
        }
        catch (FormatException)
        {
            return null; // a malformed document is a miss, not a crash
        }
    }

    internal static TimeSpan BodyDuration(string ttml)
    {
        var marker = "<body";
        var start = ttml.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return TimeSpan.Zero;

        var end = ttml.IndexOf('>', start);
        if (end < 0)
            return TimeSpan.Zero;

        var header = ttml[start..end];
        var durIndex = header.IndexOf("dur=\"", StringComparison.Ordinal);
        if (durIndex < 0)
            return TimeSpan.Zero;

        var valueStart = durIndex + 5;
        var valueEnd = header.IndexOf('"', valueStart);
        if (valueEnd < 0)
            return TimeSpan.Zero;

        return TtmlParser.ParseTime(header[valueStart..valueEnd]);
    }

    private static DateTimeOffset WrittenAt(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }
}
```

Add to `src/APMLyrics/Core/CatalogClient.cs`'s class line: `public sealed class CatalogClient : ICatalogClient`. (The `ICatalogClient` and `IAppleLyricsCache` interfaces are declared in Task 6 alongside their implementers, so this task only consumes them.)

- [ ] **Step 4: Write the SMTC source**

`src/APMLyrics/Playback/IPlaybackSource.cs`:

```csharp
using APMLyrics.Core;

namespace APMLyrics.Playback;

/// <summary>Where track and position events come from. One implementation today.</summary>
public interface IPlaybackSource : IDisposable
{
    event Action<Track?>? TrackChanged;
    event Action<TimeSpan, bool, double>? PositionChanged; // position, isPlaying, rate
    Track? Current { get; }
    void Start();
}
```

`src/APMLyrics/Playback/SmtcPlaybackSource.cs`:

```csharp
using APMLyrics.Core;
using Windows.Media.Control;

namespace APMLyrics.Playback;

/// <summary>
/// Wraps the Windows media session API. Polls at 4Hz rather than depending on
/// the event surface alone: SMTC events are irregular for WinUI players, and a
/// poll is the only way to keep the position fresh. Track identity is read from
/// the media properties on every tick and compared, which is cheap and reliable.
/// </summary>
public sealed class SmtcPlaybackSource : IPlaybackSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const string AppleMusicSessionMarker = "AppleMusicWin";

    private readonly CancellationTokenSource _cts = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Task? _loop;
    private string _lastIdentity = string.Empty;

    public event Action<Track?>? TrackChanged;
    public event Action<TimeSpan, bool, double>? PositionChanged;

    public Track? Current { get; private set; }

    public void Start() => _loop ??= Task.Run(PollLoopAsync);

    private async Task PollLoopAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception)
        {
            TrackChanged?.Invoke(null);
            return;
        }

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync();
            }
            catch (Exception)
            {
                // A transient SMTC failure must not kill the loop.
            }

            try
            {
                await Task.Delay(PollInterval, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollOnceAsync()
    {
        var session = FindAppleMusicSession();
        if (session is null)
        {
            if (Current is not null)
            {
                Current = null;
                TrackChanged?.Invoke(null);
            }
            return;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var timeline = session.GetTimelineProperties();
        var playback = session.GetPlaybackInfo();

        var identity = $"{props.Title}\u241F{props.Artist}";
        if (!string.Equals(identity, _lastIdentity, StringComparison.Ordinal))
        {
            _lastIdentity = identity;
            Current = new Track(
                props.Title ?? string.Empty,
                props.Artist ?? string.Empty,
                props.AlbumTitle ?? string.Empty,
                timeline.EndTime,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            TrackChanged?.Invoke(Current);
        }
        else if (Current is not null)
        {
            Current = Current with
            {
                Duration = timeline.EndTime,
                IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            };
        }

        if (Current is not null)
        {
            PositionChanged?.Invoke(
                timeline.Position,
                Current.IsPlaying,
                playback.PlaybackRate ?? 1.0);
        }
    }

    private GlobalSystemMediaTransportControlsSession? FindAppleMusicSession()
    {
        var sessions = _manager?.GetSessions();
        if (sessions is null)
            return null;

        foreach (var session in sessions)
        {
            if (session.SourceAppUserModelId.Contains(AppleMusicSessionMarker, StringComparison.OrdinalIgnoreCase))
                return session;
        }
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Shutdown must be safe.
        }
        _cts.Dispose();
    }
}
```

- [ ] **Step 5: Run to verify the resolver tests pass**

Run: `dotnet test --filter LyricsResolverTests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add SMTC playback source and lyrics resolver"
```

---

### Task 8: Overlay window

**Files:**
- Create: `src/APMLyrics/Ui/OverlayWindow.xaml`, `src/APMLyrics/Ui/OverlayWindow.xaml.cs`, `src/APMLyrics/Ui/ClickThrough.cs`

**Interfaces:**
- Consumes: `Core.Track`, `Core.LyricsDoc`, `Ui.LineWindow`, `Config.AppSettings`.
- Produces:
  - `class OverlayWindow : Window` with `void Apply(Track? track)`, `void Apply(LyricsDoc? doc)`, `void NoLyrics()`, `void Tick(TimeSpan position)`, `void SetClickThrough(bool enabled)`, `void ApplySettings(AppSettings settings)`, `event Action<AppSettings> SettingsChanged`.

- [ ] **Step 1: Write the click-through P/Invoke helper**

`src/APMLyrics/Ui/ClickThrough.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace APMLyrics.Ui;

/// <summary>
/// Toggles WS_EX_TRANSPARENT so the overlay stops receiving mouse input.
/// The tray menu is the only way back, because a click-through window cannot be clicked.
/// </summary>
public static class ClickThrough
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    public static void Apply(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
            return;

        var style = GetWindowLong(handle, GwlExStyle);

        // Always keep it out of Alt+Tab and non-activating; OBS and gaming
        // capture both behave better with these set.
        style |= WsExToolWindow | WsExNoActivate;
        style = enabled
            ? style | WsExTransparent
            : style & ~WsExTransparent;

        SetWindowLong(handle, GwlExStyle, style);
    }
}
```

- [ ] **Step 2: Write the overlay window**

`src/APMLyrics/Ui/OverlayWindow.xaml`:

```xml
<Window x:Class="APMLyrics.Ui.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="APM Lyrics"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="CanResize"
        MinWidth="240"
        MinHeight="60"
        SizeToContent="Manual">
    <Border x:Name="Backdrop"
            CornerRadius="8"
            Padding="16,10"
            MouseLeftButtonDown="Backdrop_MouseLeftButtonDown">
        <Grid>
            <Grid x:Name="MultiLinePanel">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <TextBlock x:Name="PrevLine" Grid.Row="0" TextAlignment="Center" TextTrimming="CharacterEllipsis"/>
                <TextBlock x:Name="CurrLine" Grid.Row="1" TextAlignment="Center" TextWrapping="Wrap" Margin="0,6"/>
                <TextBlock x:Name="NextLine" Grid.Row="2" TextAlignment="Center" TextTrimming="CharacterEllipsis"/>
            </Grid>
            <TextBlock x:Name="SingleLinePanel" TextAlignment="Center" TextWrapping="Wrap" Visibility="Collapsed"/>
        </Grid>
    </Border>
</Window>
```

`src/APMLyrics/Ui/OverlayWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using APMLyrics.Config;
using APMLyrics.Core;

namespace APMLyrics.Ui;
public partial class OverlayWindow : Window
{
    private AppSettings _settings = AppSettings.Default;
    private LyricsDoc? _doc;
    private string _trackTitle = string.Empty;
    private string _trackArtist = string.Empty;
    private TimeSpan _position;
    private int _lastIndex = -2;
    private bool _loading;

    public OverlayWindow()
    {
        InitializeComponent();
        ApplySettings(_settings);
    }

    /// <summary>Raised when the user moves or resizes, so settings can be persisted.</summary>
    public event Action<AppSettings>? SettingsChanged;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        PrevLine.FontFamily = CurrLine.FontFamily = NextLine.FontFamily = SingleLinePanel.FontFamily = new FontFamily(settings.FontFamily);
        PrevLine.FontSize = NextLine.FontSize = settings.FontSize * 0.7;
        CurrLine.FontSize = SingleLinePanel.FontSize = settings.FontSize;

        CurrLine.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.CurrentLineColor));
        PrevLine.Foreground = NextLine.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.TextColor));
        PrevLine.Opacity = NextLine.Opacity = settings.DimNeighbours ? settings.NeighbourOpacity : 1.0;
        SingleLinePanel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.CurrentLineColor));

        var backdropColor = (Color)ColorConverter.ConvertFromString(settings.BackdropColor);
        backdropColor.A = (byte)(Math.Clamp(settings.BackdropOpacity, 0, 1) * 255);
        Backdrop.Background = new SolidColorBrush(backdropColor);

        var multi = settings.Mode == DisplayMode.MultiLine;
        MultiLinePanel.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        SingleLinePanel.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;

        if (Width <= 0) Width = settings.Width;
        if (Height <= 0) Height = settings.Height;

        _lastIndex = -2; // force a refresh with the new styling
        Render();
    }

    public void SetClickThrough(bool enabled) => ClickThrough.Apply(this, enabled);

    /// <summary>Track change: clear immediately so the previous song's lines never linger.</summary>
    public void Apply(Track? track)
    {
        _doc = null;
        _loading = track is not null;
        _trackTitle = track?.Title ?? string.Empty;
        _trackArtist = Core.TrackIdentity.SplitArtist(track?.Artist ?? string.Empty);
        _lastIndex = -2;
        Render();
    }

    public void Apply(LyricsDoc? doc)
    {
        _loading = false;
        _doc = doc;
        _lastIndex = -2;
        Render();
    }

    /// <summary>Resolution finished with no match: an explicit empty state, per spec section 7.</summary>
    public void NoLyrics()
    {
        _doc = null;
        _loading = false;
        _lastIndex = -2;
        Render();
    }

    public void Tick(TimeSpan position)
    {
        _position = position;
        Render();
    }

    private void Render()
    {
        if (_doc is null || _doc.Lines.Count == 0)
        {
            // Three distinct states: nothing playing, loading, and no lyrics.
            // A stale line from the previous track is never one of them.
            string text;
            if (string.IsNullOrEmpty(_trackTitle))
                text = "Nothing playing";
            else if (_loading)
                text = $"{_trackTitle}\n{_trackArtist}";
            else
                text = $"{_trackTitle}\nNo lyrics for this song";

            PrevLine.Text = string.Empty;
            NextLine.Text = string.Empty;
            CurrLine.Text = text;
            SingleLinePanel.Text = text;
            return;
        }

        var index = LineWindow.CurrentIndex(_doc.Lines, _position);
        if (index == _lastIndex)
            return;

        _lastIndex = index;

        var current = index >= 0 ? _doc.Lines[index].Text : string.Empty;
        CurrLine.Text = current;
        SingleLinePanel.Text = current;

        if (_settings.Mode == DisplayMode.MultiLine)
        {
            PrevLine.Text = index - 1 >= 0 ? _doc.Lines[index - 1].Text : string.Empty;
            NextLine.Text = index + 1 < _doc.Lines.Count ? _doc.Lines[index + 1].Text : string.Empty;
        }
    }

    private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Raised when the button is released before the drag starts. Safe to ignore.
        }
        SettingsChanged?.Invoke(_settings with { X = Left, Y = Top });
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        SettingsChanged?.Invoke(_settings with { Width = Width, Height = Height });
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/APMLyrics`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: add always-on-top overlay window with drag, resize, click-through"
```

---

### Task 9: Tray icon and settings window

**Files:**
- Create: `src/APMLyrics/Ui/TrayIcon.cs`, `src/APMLyrics/Ui/SettingsWindow.xaml`, `src/APMLyrics/Ui/SettingsWindow.xaml.cs`
- Modify: `src/APMLyrics/APMLyrics.csproj` (add the tray icon package)

**Interfaces:**
- Consumes: `Config.AppSettings`, `Ui.OverlayWindow`.
- Produces:
  - `class TrayIcon : IDisposable` constructed with `TrayIcon(OverlayWindow overlay, Func<AppSettings> get, Action<AppSettings> set)`; wires show/hide, click-through toggle, display-mode toggle, settings, quit.
  - `class SettingsWindow : Window` with `SettingsWindow(AppSettings settings)`, `event Action<AppSettings> Applied`.

- [ ] **Step 1: Add the tray package**

```bash
cd /d/SOFTWARE/apm-lyrics
dotnet add src/APMLyrics/APMLyrics.csproj package H.NotifyIcon.Wpf --version 2.1.4
```
If that exact version is unavailable, run `dotnet package search H.NotifyIcon.Wpf --exact-match` to find the current 2.x and pin it in the csproj rather than leaving the version floating.

- [ ] **Step 2: Write the tray icon**

`src/APMLyrics/Ui/TrayIcon.cs`:

```csharp
using System.Windows;
using APMLyrics.Config;
using H.NotifyIcon;

namespace APMLyrics.Ui;

/// <summary>
/// The only way to control the app once the overlay is click-through.
/// Every item either does something real or is not present.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIcon(OverlayWindow overlay, Func<AppSettings> get, Action<AppSettings> set)
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "APM Lyrics",
            Icon = System.Drawing.SystemIcons.Application,
        };

        var menu = new System.Windows.Controls.ContextMenu();

        void Add(string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Show / hide overlay", () =>
        {
            overlay.Visibility = overlay.Visibility == Visibility.Visible
                ? Visibility.Hidden
                : Visibility.Visible;
        });

        var clickThroughItem = new System.Windows.Controls.MenuItem();
        void SyncClickThroughLabel()
        {
            var enabled = get().ClickThrough;
            clickThroughItem.Header = enabled
                ? "Click-through: ON (overlay ignores the mouse)"
                : "Click-through: OFF";
        }

        clickThroughItem.Click += (_, _) =>
        {
            var next = get() with { ClickThrough = !get().ClickThrough };
            set(next);
            overlay.SetClickThrough(next.ClickThrough);
            SyncClickThroughLabel();
        };
        menu.Items.Add(clickThroughItem);

        var modeItem = new System.Windows.Controls.MenuItem();
        void SyncModeLabel()
        {
            modeItem.Header = get().Mode == DisplayMode.MultiLine
                ? "Display: multi-line"
                : "Display: single-line";
        }

        modeItem.Click += (_, _) =>
        {
            var next = get() with
            {
                Mode = get().Mode == DisplayMode.MultiLine ? DisplayMode.SingleLine : DisplayMode.MultiLine,
            };
            set(next);
            overlay.ApplySettings(next);
            SyncModeLabel();
        };
        menu.Items.Add(modeItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        Add("Settings", () =>
        {
            var window = new SettingsWindow(get());
            window.Applied += updated =>
            {
                set(updated);
                overlay.ApplySettings(updated);
                overlay.SetClickThrough(updated.ClickThrough);
                SyncClickThroughLabel();
                SyncModeLabel();
            };
            window.Show();
        });

        Add("Quit", () => Application.Current.Shutdown());

        _icon.ContextMenu = menu;
        SyncClickThroughLabel();
        SyncModeLabel();
        _icon.ForceCreate();
    }

    public void Dispose() => _icon.Dispose();
}
```

- [ ] **Step 3: Write the settings window**

`src/APMLyrics/Ui/SettingsWindow.xaml`:

```xml
<Window x:Class="APMLyrics.Ui.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="APM Lyrics settings"
        Width="440" Height="520"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="True">
    <StackPanel Margin="20" >
        <TextBlock Text="Display mode" FontWeight="SemiBold"/>
        <RadioButton x:Name="ModeMulti" Content="Multi-line (previous, current, next)" GroupName="mode" Margin="0,6,0,0" IsChecked="True"/>
        <RadioButton x:Name="ModeSingle" Content="Single-line" GroupName="mode" Margin="0,4,0,12"/>

        <TextBlock Text="Font size (10 to 96)" FontWeight="SemiBold"/>
        <Slider x:Name="FontSize" Minimum="10" Maximum="96" TickFrequency="2" IsSnapToTickEnabled="True" Margin="0,6,0,12"/>

        <TextBlock Text="Backdrop opacity (higher is more readable)" FontWeight="SemiBold"/>
        <Slider x:Name="Backdrop" Minimum="0" Maximum="1" TickFrequency="0.05" IsSnapToTickEnabled="True" Margin="0,6,0,12"/>

        <TextBlock Text="Dim the neighbouring lines" FontWeight="SemiBold"/>
        <CheckBox x:Name="Dim" Margin="0,6,0,12"/>

        <TextBlock Text="Click-through (the tray menu can undo this)" FontWeight="SemiBold"/>
        <CheckBox x:Name="Through" Margin="0,6,0,16"/>

        <Button x:Name="Apply" Content="Apply" Height="34" IsDefault="True"/>
    </StackPanel>
</Window>
```

`src/APMLyrics/Ui/SettingsWindow.xaml.cs`:

```csharp
using System.Windows;
using APMLyrics.Config;

namespace APMLyrics.Ui;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _initial;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _initial = settings;

        ModeMulti.IsChecked = settings.Mode == DisplayMode.MultiLine;
        ModeSingle.IsChecked = settings.Mode == DisplayMode.SingleLine;
        FontSize.Value = settings.FontSize;
        Backdrop.Value = settings.BackdropOpacity;
        Dim.IsChecked = settings.DimNeighbours;
        Through.IsChecked = settings.ClickThrough;

        Apply.Click += (_, _) => Applied?.Invoke(Build());
    }

    public event Action<AppSettings>? Applied;

    private AppSettings Build() => _initial with
    {
        Mode = ModeMulti.IsChecked == true ? DisplayMode.MultiLine : DisplayMode.SingleLine,
        FontSize = FontSize.Value,
        BackdropOpacity = Backdrop.Value,
        DimNeighbours = Dim.IsChecked == true,
        ClickThrough = Through.IsChecked == true,
    };
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/APMLyrics`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add tray icon and settings window"
```

---

### Task 10: Wire it together in App

**Files:**
- Create: `src/APMLyrics/App.xaml`, `src/APMLyrics/App.xaml.cs`
- Modify: `src/APMLyrics/APMLyrics.csproj` (switch `OutputType` from `Library` to `WinExe`; the WPF SDK picks `App.xaml` up as the application definition and generates `Main`)

**Interfaces:**
- Consumes: every prior task.
- Produces: a runnable app where `dotnet run --project src/APMLyrics` shows the overlay.

- [ ] **Step 1: Write App.xaml**

`src/APMLyrics/App.xaml`:

```xml
<Application x:Class="APMLyrics.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources/>
</Application>
```

- [ ] **Step 2: Switch the project to an executable**

In `src/APMLyrics/APMLyrics.csproj`, change `<OutputType>Library</OutputType>` to `<OutputType>WinExe</OutputType>`. The WPF SDK auto-detects `App.xaml` as the application definition and generates the entry point, so no `Main` is written by hand.

- [ ] **Step 3: Write App.xaml.cs**

`src/APMLyrics/App.xaml.cs`:

```csharp
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using APMLyrics.Config;
using APMLyrics.Core;
using APMLyrics.Playback;
using APMLyrics.Ui;

namespace APMLyrics;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private OverlayWindow? _overlay;
    private TrayIcon? _tray;
    private SmtcPlaybackSource? _playback;
    private AppleLyricsCache? _cache;
    private CatalogClient? _catalog;
    private LyricsResolver? _resolver;
    private PlaybackClock? _clock;
    private AppSettings _settings = AppSettings.Default;
    private CancellationTokenSource? _resolveCts;
    private DispatcherTimer? _saveTimer;
    private DateTimeOffset? _trackStartedAt;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "APMLyricsSingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        _settings = AppSettingsStore.Load(AppSettingsStore.DefaultPath);

        // Settings are written on a debounce: a resize drag raises changes
        // continuously and must not write the file on every frame.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer!.Stop();
            AppSettingsStore.Save(AppSettingsStore.DefaultPath, _settings);
        };

        _overlay = new OverlayWindow
        {
            Left = _settings.X,
            Top = _settings.Y,
            Width = _settings.Width,
            Height = _settings.Height,
        };
        _overlay.ApplySettings(_settings);
        _overlay.SettingsChanged += OnOverlayChanged;
        _overlay.Show();
        _overlay.SetClickThrough(_settings.ClickThrough);

        _tray = new TrayIcon(_overlay, () => _settings, OnOverlayChanged);

        _cache = new AppleLyricsCache();
        _catalog = new CatalogClient();
        _resolver = new LyricsResolver(_cache, _catalog);
        _clock = new PlaybackClock(new SystemTimeSource());

        _cache.NewFile += _ =>
        {
            // Apple writes the lyric file mid-track: re-resolve so it appears
            // without waiting for the next song.
            ReResolve();
        };

        _playback = new SmtcPlaybackSource();
        _playback.TrackChanged += OnTrackChanged;
        _playback.PositionChanged += OnPositionChanged;
        _playback.Start();
    }

    private void OnOverlayChanged(AppSettings settings)
    {
        _settings = settings;
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    private void OnTrackChanged(Track? track)
    {
        _overlay?.Apply(track); // clears lines immediately, no stale lyrics
        _trackStartedAt = track is null ? null : DateTimeOffset.UtcNow;
        if (track is not null)
            ReResolve();
    }

    private void OnPositionChanged(TimeSpan position, bool isPlaying, double rate)
    {
        _clock?.Sync(position, isPlaying, rate);
        if (_clock is not null)
            _overlay?.Tick(_clock.Position);
    }

    private async void ReResolve()
    {
        if (_resolver is null || _playback?.Current is not { } track || _overlay is null)
            return;

        _resolveCts?.Cancel();
        _resolveCts = new CancellationTokenSource();
        var token = _resolveCts.Token;
        var startedAt = _trackStartedAt;

        try
        {
            var doc = await _resolver.ResolveAsync(track, token, startedAt);
            if (token.IsCancellationRequested)
                return;

            if (doc is null)
                _overlay.NoLyrics();
            else
                _overlay.Apply(doc);
        }
        catch (OperationCanceledException)
        {
            // A newer track won; this resolution is obsolete.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _saveTimer?.Stop();
        AppSettingsStore.Save(AppSettingsStore.DefaultPath, _settings);
        _tray?.Dispose();
        _playback?.Dispose();
        _cache?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Run it with Apple Music playing**

Run: `dotnet run --project src/APMLyrics`
Expected: an overlay appears showing the current Apple Music line. Verify with the manual checklist in Task 11.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: wire playback, resolution, and overlay together at startup"
```

---

### Task 11: Manual checklist, installer, and CI

**Files:**
- Create: `install/APMLyrics.iss`, `.github/workflows/build.yml`, `docs/manual-testing.md`

**Interfaces:**
- Consumes: the built app.
- Produces: a shippable release path and a recorded manual verification.

- [ ] **Step 1: Write the manual checklist and run it**

Create `docs/manual-testing.md`:

```markdown
# APM Lyrics manual checklist

Run once per release on a real Apple Music session. Record the date and result.

- [ ] Overlay appears on launch and shows the current line.
- [ ] Drag from the centre of the overlay: it moves, and the position persists after restart.
- [ ] Resize from all four edges and all four corners: the text reflows, minimum size holds.
- [ ] Play a song with lyrics: the current line advances in time with the audio.
- [ ] Skip to the next track: the previous lines clear immediately, a loading state shows, then the new lines appear.
- [ ] Play a song with no lyrics: "No lyrics for this song" or the title state shows, never the previous song.
- [ ] Pause: the highlight stops advancing. Resume: it continues from the right place.
- [ ] Toggle click-through from the tray: clicks pass through the overlay to the window below.
- [ ] With click-through on, use the tray menu to turn it off: the overlay is clickable again.
- [ ] Open a maximized and a full-screen window: the overlay stays on top.
- [ ] Switch to single-line mode: the overlay shows one line at the configured size.
- [ ] Close and relaunch: position, size, mode, and colours restore.
- [ ] If a second monitor at a different scale is available: the text is crisp on both.
- [ ] Quit from the tray: the process exits with no orphaned window.
```

Run the checklist against a live session. Fill it in. This is the evidence for spec R-35 and the manual portion of the test plan.

- [ ] **Step 2: Write the installer script**

`install/APMLyrics.iss`:

```ini
[Setup]
AppName=APM Lyrics
AppVersion=0.1.0
AppPublisher=APM Lyrics contributors
DefaultDirName={autopf}\APM Lyrics
DefaultGroupName=APM Lyrics
OutputDir=Output
OutputBaseFilename=APMLyricsSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64bitMode=yes
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\APMLyrics.exe

[Files]
Source: "..\src\APMLyrics\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\APM Lyrics"; Filename: "{app}\APMLyrics.exe"
Name: "{autostartup}\APM Lyrics"; Filename: "{app}\APMLyrics.exe"; Tasks: autostart

[Tasks]
Name: "autostart"; Description: "Start APM Lyrics when Windows starts"; Flags: unchecked

[Run]
Filename: "{app}\APMLyrics.exe"; Description: "Launch APM Lyrics"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 3: Write the CI workflow**

`.github/workflows/build.yml`:

```yaml
name: build

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore -c Release
      - name: Test
        run: dotnet test --no-build -c Release --logger "console;verbosity=normal"
```

- [ ] **Step 4: Publish and build the installer**

```bash
dotnet publish src/APMLyrics -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o src/APMLyrics/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish
# If Inno Setup is installed:
# iscc install/APMLyrics.iss
```

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore: add manual checklist, installer script, and CI workflow"
```

---

## Plan self-review

Run before execution, and again if tasks change.

**Spec coverage.** Section 2 (platform facts) is encoded as the fixtures and matcher tests in Tasks 2 and 4. Section 3 (architecture) is Tasks 2 to 10, one unit each. Section 4.2 (resolution order) is Task 4 plus Task 7: title and artist first, duration tiebreak, the fresh-file tier, and the null case. Section 4.3 (position) is Task 3 plus Task 10 wiring. Section 5 (overlay) is Task 8, with persistence in Task 10. Section 6 (settings and tray) is Tasks 5 and 9. Section 7 (errors) is spread through Tasks 6, 7, and 8. Section 8 (testing) is every task plus Task 11's checklist. Section 9 (stack and packaging) is Tasks 1 and 11.

**Review Focus coverage.** Empty lyric line: Task 2. Clock versus bare-second timestamps: Task 2. Artist album suffix: Task 4. Near-identical durations: Task 4 and Task 7. Stale lyrics on track change: Task 7 (null result) and Task 8 (Apply clears immediately).

**Type consistency.** `Track`, `LyricLine`, `LyricsDoc`, `Candidate`, `TrackInfo`, and `ResolvedCandidate` are defined once in Tasks 2 and 4 and used with the same shape later. `LineWindow.Range` is consumed in Task 8 as `First / Current / Last`. `AppSettings` is defined in Task 5 and used in Tasks 8, 9, and 10 with the same field names. `IPlaybackSource` is defined and implemented in Task 7 and consumed in Task 10.

**Placeholder scan.** No `TBD`, no `TODO`, no "similar to Task N" without the code, no step that describes without showing.

