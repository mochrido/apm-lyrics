# APM Lyrics — Design Spec

A persistent, draggable, resizable always-on-top lyrics overlay for Apple Music on Windows.

Status: approved design, ready for implementation planning
Date: 2026-10-05
Repository: https://github.com/mochrido/apm-lyrics

## 1. Problem and intent

Apple Music on Windows shows synced lyrics, but only inside its own window. There is no way to keep
the current line visible while doing something else. This spec describes a small Windows app that
floats the current lyric line above other windows, remembers where the user put it, and gets out of
the way when the user wants to work underneath it.

The intended outcome: a person plays music in Apple Music, keeps APM Lyrics parked in a corner of the
screen, and reads the current line at a glance without switching windows. It is built to be shared:
public repository, installer, README, no Apple ID required, no network dependency for the core path.

### 1.1 Goal

Display the currently playing Apple Music track's synced lyrics in a borderless, always-on-top
overlay window that can be dragged anywhere, resized from its edges or corners, and toggled to
click-through so the desktop underneath stays usable.

### 1.2 Non-goals for v1

Translations, karaoke syllable timing, global hotkeys, launch-at-login, theming engine, per-monitor
profiles, Spotify or other players, history, and online lyric search. Each is deferred deliberately,
not forgotten. Section 10 records them.

## 2. Verified platform facts

Every statement in this section was confirmed by direct measurement on the target machine
(Windows 11 build 10.0.26300, Apple Music for Windows 1.1540.23042.0) on 2026-10-05. These are the
load-bearing assumptions of the design, so they are recorded with their evidence.

### 2.1 Windows exposes Apple Music playback through SMTC

`GlobalSystemMediaTransportControlsSessionManager` reports one session per player. The Apple Music
session id is `AppleInc.AppleMusicWin_nzyj5cx40ttqa!App`. A live read returned title, artist file
position, and playback state:

```
session: AppleInc.AppleMusicWin_nzyj5cx40ttqa!App
title  : Orbiter
artist : Noah Kahan - The Great Divide: The Last Of The Bugs
pos    : 00:04:43 / end 00:04:47
state  : Playing
```

Consequence: playback position, duration, play/pause state, and track identity all come from a
supported Windows API. No audio capture, no process injection, no memory reading.

Apple packs the album name into the `Artist` field, separated by ` - `, when the album has a suffix.
The artist must be split on `" - "` and the first segment taken. This is a known Apple Music quirk,
also handled by other players in this space.

### 2.2 Apple Music caches its own synced lyrics locally

Apple Music writes the lyrics it fetches into its package cache:

```
%LOCALAPPDATA%\Packages\AppleInc.AppleMusicWin_nzyj5cx40ttqa\AC\INetCache\<BUCKET>\ttmlLyrics*.json
```

Observed bucket names: `AJWOYA6T`, `I1XMXN31`, `LYENKSNN`, `R2E4UF0N`. Each file is a small JSON
object with three keys:

```json
{ "lyricsId": "AP_1872239909", "status": "success", "ttml": "<tt><head>...</head><body>...</body></tt>" }
```

The `ttml` value is Apple's TTML lyric document: per-line `<p begin="..." end="...">text</p>`
elements, grouped into `<div>` verse sections. `itunes:timing` is `Line`. Consequence: APM Lyrics can
display Apple's own lyrics, offline, with no Apple ID and no scraping.

### 2.3 The cache is written at track change, not on demand

New lyric files appear while a song plays. During measurement, a file named
`ttmlLyricsC8Z3CS5Q.json` (a hashed name, distinct from the numbered `ttmlLyrics[N].json` pattern)
was written mid-track. Consequence: a file system watcher on the cache directory sees a new song's
lyrics arrive within seconds. The overlay never waits on the network.

### 2.4 The lyrics id joins to a real track via the public iTunes lookup

`lyricsId` has two forms. `AP_<int>` carries an Apple Music song id. Resolving it through the public
iTunes lookup endpoint returns the full track:

| lyricsId | lookup result | TTML body duration |
|---|---|---|
| `AP_1872239909` | Spoiled, Noah Kahan, The Great Divide | 306.066s |
| `AP_1872239907` | Headed North, Noah Kahan, The Great Divide | 266.786s |
| `AP_1872239890` | Dashboard, Noah Kahan, The Great Divide | 230.787s |

Across 13 `AP_` files, the lookup duration matched the TTML body duration to the second, and the
name matched the track reported by SMTC. Consequence: an exact join exists. A cached lyric file can
be identified as a specific song.

### 2.5 Duration alone is not a unique key

Two distinct songs in the same album differ by less than half a second:

| Song | Duration |
|---|---|
| Dan | 305.718s |
| Spoiled | 306.066s |
| Deny Deny Deny | 230.690s |
| Dashboard | 230.787s |

Consequence: matching by duration alone would show the wrong lyrics. Title and artist match first,
duration only breaks ties. This is why section 4.2 orders the match keys as it does.

### 2.6 Some lyric files cannot be resolved by lookup

`lyricsId` also appears in the form `MX_46212247-48515125`. The inner numbers do not resolve through
the iTunes lookup (5 of 5 returned no result). Files in this form still carry usable TTML, but their
identity can only be inferred from timing. Consequence: two-tier resolution, section 4.2.

### 2.7 Lyrics are line-level, not word-level

Every cached document on the machine used `itunes:timing="Line"` with `<p>` line elements and zero
`<span>` elements. Consequence: word-by-word highlighting, as Apple Music shows in its own full
screen view, is not available from the cache. The overlay highlights the current line and can
animate a progress fill across that line's duration. That fill is derived from the line's start and
end times, and it is honest about being derived: it is not Apple's per-word timing.

## 3. Architecture

Single Windows executable, WPF, .NET 8, with a tray icon. Four units, each independently testable.

```
apm-lyrics (WPF app, single process)
├── SMTCTracker        reads playback state; raises TrackChanged, PositionChanged
├── LyricsResolver
│   ├── AppleLyricsCache   indexes the cache dir, watches for new files
│   ├── CatalogClient      resolves AP_ ids via iTunes lookup, disk cached
│   └── TtmlParser         TTML text to LyricsDoc, pure function
├── OverlayWindow      borderless, topmost, drag, resize, click-through
│   ├── MultiLineView      previous, current, next line
│   └── SingleLineView     current line only
└── SettingsWindow + TrayIcon
```

Interfaces, so the units can be tested alone:

```csharp
interface IPlaybackSource {
    event Action<Track?> TrackChanged;
    event Action<TimeSpan, bool> PositionChanged;   // position, isPlaying
    Track? Current { get; }
}

interface ILyricsSource {
    LyricsDoc? Resolve(Track track);
    event Action<LyricsDoc> LyricsReady;            // fired when a watch finds new lyrics
}

sealed record Track(string Title, string Artist, string Album, TimeSpan Duration, bool IsPlaying);
sealed record LyricsDoc(string LyricsId, string? Lang, IReadOnlyList<LyricLine> Lines);
sealed record LyricLine(TimeSpan Begin, TimeSpan End, string Text, string? Part);
```

Dependencies point inward: `TtmlParser` and the matcher depend on nothing but the BCL, so they carry
the tests. `SMTCTracker` wraps one Windows API. `OverlayWindow` depends on `Track` and `LyricsDoc`
only, never on the cache or the network.

## 4. Data flow

### 4.1 Track change

```
SMTC event (track changed)
  -> SMTCTracker raises TrackChanged(Track)
  -> LyricsResolver.Resolve(track):  see 4.2
       hit  -> overlay swaps lines, crossfades current line
       miss -> overlay shows title and artist with a "lyrics loading" state,
               watcher stays armed; the cache write fires LyricsReady and the lines appear
```

### 4.2 Resolution order

1. **Exact id.** If a previously resolved track maps title plus artist to a lyrics id, load that
   file directly.
2. **Title and artist.** For each cached file with an `AP_` id, resolve via `CatalogClient`
   (disk cached, so a warm cache is offline). Match on normalized title and artist. This is the
   primary key, because section 2.5 shows duration cannot separate two songs in one album.
3. **Duration tiebreak.** When title and artist match more than one candidate, pick the one whose
   duration is closest to the SMTC `EndTime`, within 2 seconds.
4. **Filename arrival heuristic.** For a file whose id cannot be resolved (`MX_` form), accept it
   only when it was written within a few seconds of the current track starting and its body duration
   is within 2 seconds of the SMTC `EndTime`. This tier is best-effort and is labeled as such in
   code.
5. **Nothing.** Show a "no lyrics available" state. Never show the previous track's lines.

Normalization for step 2: case fold, trim, collapse internal whitespace, strip a trailing `" - Single"`,
`" - EP"`, or `" - Live"` suffix from the album and from the artist field after the `" - "` split,
and compare accent-insensitively.

### 4.3 Position and highlight

`SMTCTracker` polls SMTC at 4Hz (250ms). SMTC ticks are coarse, so the overlay keeps a monotonic
clock between ticks: on each tick it records `(position, timestamp)`, and between ticks it
interpolates `position + (now - timestamp)`. Interpolation stops when the track is paused. The
current line is the last line whose `Begin` is at or before the interpolated position. A line
progress fraction, `(position - Begin) / (End - Begin)`, drives the progress fill and is clamped to
`[0, 1]`. Playback rate from `GetPlaybackInfo` is folded into the interpolation when it is not 1.0.

Repaints happen on line change and on a low-frequency progress tick, not per frame, so an idle
overlay costs almost nothing.

## 5. Overlay window

A WPF `Window` with `WindowStyle=None`, `AllowsTransparency=true`, `Background=Transparent`,
`Topmost=true`, `ShowInTaskbar=false`, and `ResizeMode=CanResize`. The last of these keeps
native edge and corner resize, including the 8px hit targets Windows users expect, with correct
per-monitor DPI through a manifest declaring PerMonitorV2.

Native resize comes from the `WS_THICKFRAME` window style, which `CanResize` sets;
`CanResizeWithGrip` sets the same style bits and only adds a grip adornment that does not render
on a borderless window, so the two values are functionally identical here and `CanResize` is the
honest description of what is in use.

**Drag.** `DragMove()` on left button press in the body, guarded by a try/catch for the
`InvalidOperationException` it raises when the button is released early. Because the window is
borderless, the whole surface is a drag handle, except interactive controls, which are None in the
default overlay.

**Resize.** Native resize from `ResizeMode`. Minimum size 240 by 60 to keep a line readable.

**Click-through.** Toggled from the tray menu, never from the overlay itself. Setting the extended
window style `WS_EX_TRANSPARENT | WS_EX_LAYERED` via `SetWindowLong` makes the overlay ignore the
mouse. The tray menu is the only way back out, since the overlay can no longer be clicked. This is
the single most confusing state a user can reach, so the tray item is labeled "Click-through
(enabled)" rather than a bare toggle, and the state is also reflected in the settings window.

**Persistence.** Position, size, display mode, font size, opacity, colours, and click-through state
are saved to `%APPDATA%\APMLyrics\settings.json` on change, debounced. Restored on launch, with a
guard that pulls the window back on screen if the saved monitor is gone.

**Clamping.** Position is clamped to the virtual screen bounds so the overlay cannot be dragged
somewhere unrecoverable.

### 5.1 Display modes

**Multi-line (default).** Previous, current, and next line. The current line is at full opacity and
emphasized. Neighbours are dimmed, scaled slightly smaller, and blurred only if it proves cheap.
Apple Music shows a longer scroll; three lines is the useful minimum and the cheapest thing that
reads as lyrics rather than as a caption.

**Single-line.** The current line only. The smallest footprint, closest to MiniLyrics.

Both modes share one renderer that takes a window of lines and a current index, so the only
difference is how many lines the window contains.

### 5.2 Visual direction

Dials: **ENERGY 2 / RHYTHM 1 / MOTION 1.** The overlay is a reading surface that sits on top of
someone else's work, so it stays restrained. Motion is limited to a short opacity crossfade on line
change and a progress fill, both serving legibility rather than decoration. This is a stated
direction, not a default.

Per the antislop rules that govern UI work in this repository: no em dash in user-facing text,
text over the overlay must meet WCAG AA contrast against whatever is behind it (a dark scrim behind
the text, not just a text shadow), and the settings window must be fully keyboard navigable with a
visible focus indicator.

Contrast over arbitrary desktop content is the sharpest risk. The design answer is a semi-opaque
backdrop whose opacity is user-adjustable, defaulting to a value that keeps body text at or above
4.5:1 against typical light and dark backgrounds, rather than relying on text shadow alone.

## 6. Settings and tray

Tray icon (in the notification area) menu:

- Show or hide overlay
- Click-through, showing its state
- Display mode: multi-line or single-line
- Settings
- Quit

Settings window: display mode, font family and size, text colour, current-line colour, neighbour
opacity, backdrop colour and opacity, minimum line count for multi-line mode, and a checkbox for
"dim neighbours". Changes apply live.

The settings window is the only place a keyboard-focused UI exists, so it carries the accessibility
requirements from section 5.2.

## 7. Error handling

| Condition | Behaviour |
|---|---|
| Apple Music not running | Overlay shows an idle state, or hides to tray per setting. No error dialog. |
| No lyrics for the track | "No lyrics for this song" state in the overlay. The previous track's lines are never reused. |
| Cache file malformed | Skip the candidate, log at debug, try the next one. |
| iTunes lookup unreachable | Fall back to the local resolution cache, which is warm after first lookup, so the app works offline after first use. |
| SMTC unavailable | Show an explicit unavailable state, not a silent empty window. |
| Floating point drift between polls | Re-sync to the authoritative SMTC position on every tick. |

## 8. Testing

**Unit tests, no UI.** `TtmlParser` and the matcher are pure functions over strings and records, so
they are the bulk of the suite.

- `TtmlParser` against captured fixtures: multi-line documents, verse grouping, `leadingSilence`,
  a missing `dur`, an empty body, malformed XML, an entity-heavy line.
- Matcher: the two collision pairs from section 2.5 as regression cases, asserting that title and
  artist win over duration; an `MX_` file matched by the arrival heuristic; a track with no
  candidate returning empty rather than the previous result.
- `CatalogClient` with a recorded HTTP fixture, and a cold cache versus warm cache path.

**Fixtures.** Real cache files are used as local, gitignored test data during development, because
they contain personal listening data. Committed fixtures are synthesized: hand-written TTML that
exercises the same features without real lyrics. No real lyric text is committed.

**Manual overlay checklist.** WPF overlay behaviours are cheaper to verify by hand than to automate.
The checklist is: drag from every edge region, resize from all eight handles, verify topmost over a
maximized window and a full-screen window, toggle click-through and confirm clicks pass through and
the tray recovers the window, close and relaunch and confirm position and size restore, and confirm
DPI-correct rendering on a second monitor at a different scale if one is available.

## 9. Tech stack and packaging

- **Language and runtime.** C# on .NET 8, WPF. Native Windows UI toolkit, about 40 to 80 MB RSS
  when idle, first-class support for borderless topmost windows and native resize.
- **Not chosen.** Electron (200 MB plus, for a text overlay) and Tauri (needs a Rust toolchain and MSVC
  build tools, and its transparent resizable overlay path is less proven). Both are viable; neither
  fits a small always-on overlay as well as the native toolkit.
- **UI library.** WPF-UI for the settings window's modern control styling. The overlay itself draws
  its own text and uses no control chrome.
- **Installer.** Inno Setup, producing a single `APMLyricsSetup.exe`, published as a GitHub release
  asset. A self-contained .NET publish avoids asking users to install a runtime.
- **Build tooling.** The .NET 8 SDK via `winget install Microsoft.DotNet.SDK.8`. Not currently
  installed on the development machine, so it is the first setup step.
- **CI.** A GitHub Actions workflow building the solution and running the unit tests on
  `windows-latest`.

## 10. Deferred

Recorded so they are choices, not omissions: translations, karaoke syllable timing from a second
source, global hotkeys, launch at login, custom themes, per-monitor profiles, Spotify and other
SMTC players, lyric offset adjustment, a lyric search by title when the cache misses, and an
Apple Music favorite or like button (which would need UI Automation, since SMTC does not expose it).

## 11. Open risks

1. **Cache bucket sets may rotate.** Apple writes into numbered cache buckets. If Apple clears or
   renumbers a bucket, an index built once goes stale. Mitigation: re-scan on track change and on a
   slow timer, rather than indexing once at startup.
2. **Apple may change the cache format.** The join in section 2.4 is a private implementation
   detail. If it changes, resolution degrades to the duration tier and then to "no lyrics".
   Mitigation: keep the parser tolerant and log rather than throw.
3. **Backdrop contrast over arbitrary content.** Section 5.2 addresses it with an adjustable scrim,
   but the default must be validated against real desktops before release.
4. **Personal data in fixtures.** Real lyric text is copyright and personal listening data.
   Mitigation: never commit real fixtures, as stated in section 8.
