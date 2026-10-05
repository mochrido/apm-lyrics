# APM Lyrics

A persistent, draggable, resizable lyrics overlay for Apple Music on Windows.

Apple Music shows synced lyrics, but only inside its own window. APM Lyrics floats the current line
on top of everything else, so you can read along while you work. It stays where you put it, resizes
from its edges, gets out of the way with a click-through mode, and works offline.

## Status

Implemented. All 11 tasks are done, and `dotnet test` passes 69 tests. The design lives in
[`docs/superpowers/specs/2026-10-05-apm-lyrics-design.md`](docs/superpowers/specs/2026-10-05-apm-lyrics-design.md).

## How it works

Windows exposes what Apple Music is playing through the system media API. Apple Music caches the
synced lyrics it fetches into its own package cache as TTML. APM Lyrics reads the playback state
from Windows, finds the matching cached lyric file, and renders the current line in a small
always-on-top window. Nothing is scraped, no Apple ID is used, and the core path needs no network.

## Features

- Always-on-top overlay, drag anywhere, resize from edges and corners
- Multi-line mode (previous, current, next) and single-line mode
- Click-through mode for when you need the desktop underneath
- Remembers position, size, and appearance
- Reads Apple Music's own cached lyrics, offline

## Requirements

- Windows 10 version 19041 or later, or Windows 11
- Apple Music for Windows
- No separate .NET runtime: the setup program is self-contained (the .NET 8 SDK is only
  needed to build from source)

## Building

Install the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`), then from the repository
root:

```bash
dotnet build
dotnet test
```

To build the installer, publish self-contained for win-x64 and compile
[`install/APMLyrics.iss`](install/APMLyrics.iss) with
[Inno Setup 6](https://jrsoftware.org/isinfo.php):

```bash
dotnet publish src/APMLyrics -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o src/APMLyrics/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish
iscc install/APMLyrics.iss
```

The setup program is written to `install/Output/APMLyricsSetup.exe`. It is 64-bit only,
because the publish target is win-x64 and there is no 32-bit build.

## License

MIT. See [LICENSE](LICENSE).

## Acknowledgements

The idea of an always-on-top lyric overlay is not new. This project is deliberately scoped to Apple
Music on Windows, and it reads Apple Music's own cached lyrics rather than a third-party source.
