# Security policy

## Reporting a problem

Please do not open a public issue for a security problem. Use GitHub's
[private vulnerability reporting](https://github.com/mochrido/apm-lyrics/security/advisories/new)
instead, or contact the maintainer through their GitHub profile.

## What this app can and cannot do

Useful context for judging severity:

- It makes **two kinds of outbound request**: a lookup against Apple's public iTunes API, used to
  learn the title and artist behind a cached lyric file's ID, and a search/download against
  [LRCLIB](https://lrclib.net) when Apple has not cached timed lyrics for the playing track.
  LRCLIB requests carry only the track title, artist, album and duration needed to find a match.
  Nothing about you is sent.
- It has **no Apple ID, no account, and no credentials** of any kind.
- It **reads** Apple Music's local cache. It never writes to it.
- Its own written files are settings under `%APPDATA%\APMLyrics` and accepted fallback lyrics under
  `%APPDATA%\APMLyrics\lyrics-cache`. Cached lyric files are named by a hash of the recording
  identity, never by the song title, and every cached entry is revalidated before use.
- The installer is not code-signed, so Windows will warn on first run. That is expected and is
  not a vulnerability in itself.

## Supported versions

Only the latest release is supported.
