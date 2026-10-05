# APM Lyrics

A persistent, draggable, resizable lyrics overlay for Apple Music on Windows.

Apple Music shows synced lyrics, but only inside its own window. APM Lyrics floats the current line
on top of everything else, so you can read along while you work. It stays where you put it, resizes
from its edges, gets out of the way with a click-through mode, and works offline.

## Status

Design approved, implementation not started. The design lives in
[`docs/superpowers/specs/2026-10-05-apm-lyrics-design.md`](docs/superpowers/specs/2026-10-05-apm-lyrics-design.md).

## How it will work

Windows exposes what Apple Music is playing through the system media API. Apple Music caches the
synced lyrics it fetches into its own package cache as TTML. APM Lyrics reads the playback state
from Windows, finds the matching cached lyric file, and renders the current line in a small
always-on-top window. Nothing is scraped, no Apple ID is used, and the core path needs no network.

## Planned features

- Always-on-top overlay, drag anywhere, resize from edges and corners
- Multi-line mode (previous, current, next) and single-line mode
- Click-through mode for when you need the desktop underneath
- Remembers position, size, and appearance
- Reads Apple Music's own cached lyrics, offline

## Requirements

- Windows 10 version 19041 or later, or Windows 11
- Apple Music for Windows
- .NET 8 Desktop Runtime (the installer can ship self-contained, so this may be bundled)

## Building

Not yet available. The first build step is `winget install Microsoft.DotNet.SDK.8`, then
`dotnet build` on the solution.

## License

MIT. See [LICENSE](LICENSE).

## Acknowledgements

The idea of an always-on-top lyric overlay is not new. This project is deliberately scoped to Apple
Music on Windows, and it reads Apple Music's own cached lyrics rather than a third-party source.
