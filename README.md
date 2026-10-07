# APM Lyrics

**Synced lyrics for Apple Music, floating on top of everything else on your Windows desktop.**

Apple Music shows its lyrics in a panel inside its own window. APM Lyrics takes the same lyrics
and puts them in a small always-on-top window you can drag anywhere, resize to any shape, and
click straight through when you need the desktop underneath.

```
+-------------------------------------------+
|  And I know it's not much          <- previous line, dimmed
|  But it's the best I'll ever do    <- current line, bright
|  And I know it's not much          <- next line, dimmed
+-------------------------------------------+
```

It reads the lyrics **Apple Music itself already downloaded to your PC**. It does not scrape
Apple's servers, does not ask for your Apple ID, and keeps working when your connection drops.

---

## Install

1. Download `APMLyricsSetup.exe` from the [latest release](../../releases/latest).
2. Run it. Windows may show a blue "Windows protected your PC" box, because this app is not
   code-signed. Click **More info**, then **Run anyway**.
3. Play something in Apple Music. The lyrics appear on their own.

No separate .NET download is needed; the installer is self-contained.

Code-signing certificates cost a few hundred dollars a year, which did not seem worth it for a
lyrics overlay. The full source is here if you would rather read it than trust it.

## Using it

Everything is controlled from the **tray icon** near your clock (it may be hiding behind the `^`
arrow). Right-click it for:

| Menu item | What it does |
|---|---|
| Show / hide overlay | Puts the lyrics away without quitting |
| Click-through: ON / OFF | Makes the overlay ignore the mouse, so you can click the window behind it |
| Mode | Multi-line (previous/current/next) or single-line |
| Settings | Font, colours, and how many neighbour lines to show |
| Quit | Exits the app |

**Moving it:** drag from anywhere on the overlay.

**Resizing it:** grab any edge or corner. The text resizes with the window, so it always fills
the space; you can shrink it down to a strip about as tall as the taskbar.

**Click-through gotcha:** once click-through is on, the overlay cannot be clicked at all. The
tray menu is the only way to switch it back off.

## How it works

You do not need this section to use the app. It is here because "where do the lyrics come
from?" is the first question most people ask.

1. **Ask Windows what is playing.** Windows has a system media API that any player can report
   to. APM Lyrics polls it four times a second to learn the track, artist, position, and length.

2. **Find that track's lyrics locally.** When Apple Music shows you lyrics, it downloads them as
   a small [TTML](https://en.wikipedia.org/wiki/Timed_Text_Markup_Language) file and caches it on
   disk. APM Lyrics scans that cache, works out which file belongs to the playing track, and
   parses the timings.

3. **Draw the right line.** Each lyric line carries a start and end time. A small interpolating
   clock smooths the coarse four-per-second updates from Windows, and the overlay highlights
   whichever line covers the current moment.

### Matching a song to its lyrics

Apple's cached files are named by an internal ID, not by song title, so the app has to work out
which file is which. It asks Apple's public iTunes lookup service for the title and artist behind
each ID, then compares those against what the player reported. When two songs on one album are
half a second apart in length, title and artist are what separate them; duration only breaks
ties.

Two details caused real bugs during development, and are worth knowing if you read the code:

- Apple reports the artist field as `Artist <em dash> Album`, using the U+2014 character, not a
  hyphen. The first version looked for a hyphen, and so matched nothing on albums that carry the
  suffix.
- Windows reports a track's length as **zero** for the first couple of seconds after a track
  changes. Filtering on that zero rejected every candidate, so an unknown length is now treated
  as "do not filter on length" rather than "zero seconds long".

### What it deliberately does not do

Global hotkeys, launching at login, lyric search for songs Apple has not cached, Spotify and
other players, and custom themes are all out of scope. The design document lists them so they
read as decisions rather than oversights.

## Project layout

```
src/APMLyrics/
  Core/        Lyrics parsing, song matching, cache index  (pure logic, no UI)
  Playback/    Talking to the Windows media API
  Config/      Settings load and save
  Ui/          The overlay window, tray icon, settings window
tests/         Unit tests for everything in Core and Playback
docs/          Design document, implementation plan, manual test checklist
install/       Installer script
```

The split matters: `Core` and `Playback` have no UI dependencies, so the interesting logic can be
tested without a screen. That is where the tests live.

## Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
(`winget install Microsoft.DotNet.SDK.8`).

```bash
git clone https://github.com/mochrido/apm-lyrics.git
cd apm-lyrics

dotnet build                          # compile
dotnet test                           # run the 69 unit tests
dotnet run --project src/APMLyrics    # run it without installing
```

To produce the installer, publish a self-contained build and compile the Inno Setup script:

```bash
dotnet publish src/APMLyrics -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false \
  -o src/APMLyrics/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish

iscc install/APMLyrics.iss     # requires Inno Setup 6
```

The result is `install/Output/APMLyricsSetup.exe`. It is 64-bit only, because there is no 32-bit
publish target.

## Requirements

- Windows 10 version 19041 or newer, or Windows 11
- [Apple Music for Windows](https://apps.microsoft.com/detail/9pfhdd62mxs1) installed and signed in
- A track must have been played once in Apple Music before its lyrics exist in the cache

## Contributing

Issues and pull requests are welcome. Before sending a change, run `dotnet test`; the suite runs
offline and takes under a second. The design document in
[`docs/superpowers/specs/`](docs/superpowers/specs/) explains the intended behaviour, and
[`docs/manual-testing.md`](docs/manual-testing.md) lists the things that can only be checked by
hand, on a real desktop, with music playing.

## License

MIT. See [LICENSE](LICENSE).

## Acknowledgements

Always-on-top lyric overlays are not a new idea. This project is deliberately narrow: Apple Music
on Windows, reading Apple's own cached lyrics rather than a third-party lyrics service.
