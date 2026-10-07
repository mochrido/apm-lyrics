# Security policy

## Reporting a problem

Please do not open a public issue for a security problem. Use GitHub's
[private vulnerability reporting](https://github.com/mochrido/apm-lyrics/security/advisories/new)
instead, or contact the maintainer through their GitHub profile.

## What this app can and cannot do

Useful context for judging severity:

- It has **no network access in normal use**. The only outbound request is a lookup against
  Apple's public iTunes API, used to learn the title and artist behind a cached lyric file's
  ID. Nothing about you is sent.
- It has **no Apple ID, no account, and no credentials** of any kind.
- It **reads** Apple Music's local cache. It never writes to it.
- Its only written files are its own settings under `%APPDATA%\APMLyrics`.
- The installer is not code-signed, so Windows will warn on first run. That is expected and is
  not a vulnerability in itself.

## Supported versions

Only the latest release is supported.
