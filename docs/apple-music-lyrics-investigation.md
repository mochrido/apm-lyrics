# Apple Music lyrics investigation — 2026-10-20

Status: preliminary research, not an approved implementation design.

## Goal

Recover lyrics when Apple Music displays them but APM Lyrics does not, and follow forward/backward seeks reliably. User wants research/design now and execution with a cheaper model later.

## Observations

- Installed package: `AppleInc.AppleMusicWin`, version `1.1540.23042.0`, x64.
- Package directory: `C:\Program Files\WindowsApps\AppleInc.AppleMusicWin_1.1540.23042.0_x64__nzyj5cx40ttqa`.
- `AMP.Services.dll`: 26,918,256 bytes; SHA-256 `993c758adf6f4f68abe931a86d2647b69c3c8dd404f9717eb924b8d732c8c597`, identified by REA open_binary as native x64 PE.
- REA/Ghidra could not admit the original WindowsApps path (access denied). Copying the DLL to the approved temporary directory preserved the digest and passed admission. Full analysis then exceeded the 330,000 ms startup timeout. No successful decompilation or call-graph evidence was produced. Session closed afterward.
- A direct ASCII/UTF-16LE string scan of that DLL found `GetLyricsAsync`, `GetNowPlayingLyricsAsync`, `ContentDescriptor`, `LyricsSource`, `HasCloudLyrics`, `HasLibraryLyrics`, `CustomLyrics`, `StoreLyrics`, and `TSLLyricsSongInfo::CreateFromTTML`-related RTTI. Diagnostic strings describe requesting LyricsInfo when NowPlayingItem or ContentDescriptor changes. These are static leads, not proof of execution or an externally callable API.
- The expected current-user package cache exists. A recursive `ttmlLyrics*.json` scan returned zero files. A package-wide filename search for `*lyrics*` also returned none. This does not exclude opaque caches, other formats, hidden files omitted by the first scan, or inaccessible content.
- AppleMusic was not running at inspection time. No failing-track reproduction or live timeline measurement was obtained.

## Source-established APM issues

- `Playback/PlaybackClock.cs`: Sync never accepts a backward position; Reset only occurs on observed track changes. Genuine backward seeking therefore conflicts with the clock policy.
- `Playback/SmtcPlaybackSource.cs`: polls positions but does not use timeline LastUpdatedTime; session loss clears Current but retains the previous identity.
- `App.xaml.cs`: resolves on track changes and successful cache notifications; no scheduled bounded retry after a miss, and duration becoming available does not itself trigger resolution.
- `Core/LyricsResolver.cs`: scans all candidates and performs sequential metadata lookups before selecting one.
- Unnamed-file matching uses duration and arrival-time heuristics, not a definitive song identifier.

## Direct-download research

Context7 query of `/websites/developer_apple_applemusicapi` returned official song/library metadata examples with `hasLyrics` and `playParams.catalogId`, but no full-lyrics/TTML endpoint. This bounded result does not establish that no endpoint exists. Direct downloading remains unverified; no authenticated request was replayed and no credentials were extracted.

Source: https://developer.apple.com/documentation/applemusicapi/get-multiple-library-resources-by-resource-typed-ids-parameters

## Timeline measurement tooling (2026-10-21)

`scripts/Observe-AppleMusicTimeline.ps1` polls the Apple Music media session every
250 ms and appends JSONL records containing observation time (UTC and monotonic),
session id/app id, title/artist/album, playback status and rate, position,
start/end/min/max seek time, and timeline `LastUpdatedTime`. It is passive: it
sends no transport commands and reads no tokens. Run it with an output path
outside the repository:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Observe-AppleMusicTimeline.ps1 `
    -Seconds 90 -OutputPath "$env:TEMP\apm-timeline.jsonl"
```

Verified with Apple Music closed: eight observations were written, each with
`sessionFound:false`, and the script exited cleanly rather than failing.

### Result: pending

A live session has not yet been captured, so the following remain **unmeasured**:

- Whether `Position` is anchored at `LastUpdatedTime` or refreshed independently.
- Whether `LastUpdatedTime` repeats while `Position` changes on seek.
- Whether pausing updates `Position`.
- How quickly a seek becomes visible through SMTC.

Implementation ruling until measured: treat each observed `Position` as
authoritative at receipt, interpolate with a local monotonic clock, and leave
`LastUpdatedTime`-age compensation **disabled**. Do not invent a seek
discontinuity threshold. Revisit after a live trace.

## Next evidence needed

### Live observation: Pasti Ada Jalan — Perunggu

User selected this track on 2026-10-20. Windows SMTC confirmed title `Pasti Ada Jalan`, artist `Perunggu - Pasti Ada Jalan - Single`, playing, position 71 seconds, duration 139 seconds. Public iTunes search in both US and ID returned exact title/artist with catalog ID 1867164174 and duration 139.480 seconds.

A forced recursive package inventory found 1,071 files with zero enumeration errors and two lyric JSON files:

| Lyrics ID | Last write UTC | Timing | Body duration | Paragraphs / timed paragraphs |
|---|---|---|---|---|
| GE_11410973 | 2026-10-20T08:39:22.7414249Z | None | absent | 25 / 0 |
| MX_35536551-43311902 | 2026-10-20T08:40:12.6322516Z | Line | 230.310 seconds | 36 / 36 |

Both responses have status success and only lyricsId/ttml/status JSON keys. Exact association of either file with the selected track has NOT been established. The timed file's duration differs substantially from the current song; the untimed file has no timestamps to synchronize. Need user confirmation whether Apple Music highlights/advances lyrics for this track or displays static text.

Source implications: neither prefix is directly lookup-compatible in CatalogClient. Missing body duration becomes zero in LyricsResolver. Its search-duration gate cannot attach the 139.480-second search result to either observed candidate (zero or 230.310 seconds), and the known-duration matcher cannot accept the untimed candidate by arrival time. TtmlParser also treats missing begin/end as zero, so merely relaxing matching would not create valid synchronized lyrics. This demonstrates a concrete unsupported payload shape, not yet definitive attribution of this user's missing-lyrics incident.

1. User plays a failing song and opens its lyrics in Apple Music; record title, artist, version, and observation time.
2. Compare cache inventory before/after opening lyrics, including hidden files and changed opaque files; record enumeration errors rather than treating them as absence.
3. Identify the actual lyrics payload and its exact song linkage. Determine whether cache metadata/URL associates catalog ID with lyrics ID, avoiding duration guesses.
4. Measure Windows timeline Position, LastUpdatedTime, duration, state, and session identity during normal playback, pause, forward seek, backward seek, and rapid track switching.
5. Follow static lyrics-loading leads with focused metadata inspection or decompilation. Larger Ghidra timeout requires a server configuration change/restart; do not repeat the unchanged timed-out call.
6. Establish direct-download endpoint, supported authorization, response format, and refresh behavior before proposing it as a product dependency.

## Working direction, not yet a conclusion

Prefer exact track-to-lyrics association and a reliable local source if runtime evidence supports them. Consider direct downloading only after proving the access path. Seeking must be fixed independently of lyric acquisition: new lyrics alone cannot repair a clock that ignores backward seeks.

## Lyrica fallback investigation

Inspected https://github.com/Wilooper/Lyrica at commit `2c77f81752f3e1ae9a1adbd7b68f633d60b2de23`. MIT-licensed Python/Flask lyrics aggregation service, not a Windows playback integration. README advertises eight providers, including LRCLIB, Lrcmux, Genius, YouTube Music, NetEase, Megalobiz, Musixmatch, and Apple Music.

Source files inspected: `src/sources/apple_music_fetcher.py`, `lrclib_fetcher.py`, `lrcmux_fetcher.py`.

- LRCLIB adapter searches by artist/title, picks the first result, then fetches by returned metadata. For APM use stronger recording/version/duration checks rather than copying first-result selection.
- Lrcmux adapter requests `/get` with artist/title, sources=musixmatch, format=json, and optionally level=line/word. This is another hosted dependency; availability was not tested.
- Apple adapter calls the private `https://amp-api.music.apple.com/v1/catalog/{storefront}/songs/{id}/syllable-lyrics` endpoint. Requires a configured developer token; optionally sends Music-User-Token. No authenticated execution verified. This is a concrete research lead, not proof that a developer token alone currently grants access.
- Apple adapter ranks search results by artist substring and tries three candidates without exact title/duration validation. Parser fills missing starts with zero and missing ends with start+4000ms, potentially misrepresenting untimed input as synchronized. `_parse_p` drops `leading_space` from span dictionaries while `_build_words` accesses that key on subsequent spans, a static defect in the inspected word/syllable path. Its comment that Apple provides no plain lyrics is contradicted by our locally observed timing=None payload.
- Live LRCLIB search for Pasti Ada Jalan / Perunggu returned HTTP 503 ServerOverloaded. No conclusion about song coverage is possible from that request.

Provisional recommendation: direct LRCLIB integration is the smallest first online fallback for this C# desktop app; keep Lyrica as an optional aggregation option only if its additional provider coverage justifies a hosted service or Python deployment. Preserve genuine timing classification and independent seek correction.
