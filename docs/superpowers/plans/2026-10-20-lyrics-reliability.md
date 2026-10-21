# Lyrics Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for economical inline execution, or superpowers:subagent-driven-development if the user explicitly chooses delegation. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically obtain correctly matched synchronized lyrics and follow Apple Music playback through seeks without manual adjustment.

**Architecture:** Retain the existing C#/WPF app, Apple cache reader, and Windows media-session integration. Add a direct LRCLIB fallback and a small resolution coordinator; classify untimed content explicitly. Replace forward-only clock correction with fresh-snapshot anchoring and generation-safe updates.

**Tech Stack:** .NET 8, WPF, Windows media-session API, HttpClient, System.Text.Json, XML, existing xUnit tests. No new production dependencies.

**Spec:** `docs/superpowers/specs/2026-10-20-lyrics-reliability-design.md`

## Global Constraints

- Standalone Windows application; no Python service or hosted backend to maintain.
- No manual offset, scrollable plain lyrics, invented timestamps, or timestamp stretching.
- Prefer correctly identified timed Apple lyrics, then accepted local fallback cache, then online LRCLIB.
- Initial online duration tolerance: at most 2 seconds; unknown duration delays acceptance.
- Local retry schedule: approximately 1, 3, and 8 seconds after track start; also react once to duration becoming known.
- Proposed correction target: within 500ms after Windows exposes a new position, not necessarily after the user's gesture.
- Only genuinely associated plain lyrics justify `Only unsynced lyrics available`.
- Use synthetic test fixtures. Do not commit real lyric payloads, credentials, installed Apple binaries, or personal library files.
- User requested planning now and a cheaper model for implementation. Do not execute this plan until they switch/authorize execution.

## Review Focus

1. Repeated provider timestamps with a changed position must still permit a seek (Task 2).
2. Same-title studio/live recordings and ambiguous duplicates must not yield confident wrong lyrics (Task 4).
3. A canceled old-song request or a disconnected session must not restore stale content (Task 6).
4. A 503 or damaged local cache must not become a permanent lyric miss (Task 5).
5. Untimed or non-finite timestamp input must never become synchronized content or crash the dispatcher (Task 3).

## File map

All application paths below are relative to `src/APMLyrics/` unless a full repository-relative path is given. All commit steps require user authorization; otherwise leave reviewed changes uncommitted.

Modify `Playback/IPlaybackSource.cs`, `Playback/SmtcPlaybackSource.cs`, and `Playback/PlaybackClock.cs` for structured observations and clock correction. Modify `Core/LyricsDoc.cs`, `Core/TtmlParser.cs`, `Core/LyricsResolver.cs`, `Core/LyricsMatcher.cs`, and `Core/CatalogClient.cs` for explicit content classification and local resolution. Create `Core/LrcParser.cs`, `Core/LrclibClient.cs`, `Core/FallbackLyricsCache.cs`, and `Core/LyricsCoordinator.cs` for distinct parsing, HTTP/matching, persistence, and orchestration responsibilities. Wire them in `App.xaml.cs`; adapt `Ui/OverlayWindow.xaml.cs` and `Ui/LyricPanel.xaml.cs` for status text. Extend the existing test files and add corresponding small test files for new components. No UI redesign or unrelated settings refactor.

## Task 1: Establish baseline and capture timeline evidence

**Files:** update `docs/apple-music-lyrics-investigation.md`, `docs/manual-testing.md`; create `scripts/Observe-AppleMusicTimeline.ps1` only as a passive diagnostic tool.

**Interfaces:** diagnostic script accepts `-Seconds 60 -OutputPath <local JSONL path>`; records UTC/monotonic observation time, session ID, title/artist/album, position, duration, LastUpdatedTime, playback state/rate every 250ms. Never controls playback or reads tokens.

- [ ] Inspect `git status --short` and read the approved spec and research ledger. Preserve unrelated changes. Run `dotnet test -c Release`; previous baseline was 119 passing cases, not a requirement to preserve the exact count.
- [ ] Write the passive script using Windows PowerShell WinRT support, with a finite duration and explicit output destination outside the repo. Verify that no-session produces a readable record rather than failing.
- [ ] Ask the user to play an Apple-timed song, then pause/resume, seek forward/backward, and switch tracks while recording. Save sanitized observations and conclusions in the research ledger, not full raw personal metadata.
- [ ] Determine whether position is anchored at LastUpdatedTime or refreshed independently. Record whether timestamps repeat during changed positions and whether pause updates position. Choose age compensation only if measured; otherwise use observed position at receipt and leave compensation disabled. Do not invent a seek threshold.
- [ ] If live playback is unavailable, mark this gate pending and proceed only with independent parser/provider tasks. Final sync acceptance remains blocked; do not falsely mark this task complete.

## Task 2: Fresh playback snapshots and bidirectional correction

**Files:** modify the three `Playback/` files, `App.xaml.cs`, `tests/APMLyrics.Tests/PlaybackClockTests.cs`; add `tests/APMLyrics.Tests/PlaybackSnapshotTests.cs` if transition logic cannot be tested in the existing file.

**Interfaces:** define `PlaybackSnapshot(long Generation, long Sequence, TimeSpan Position, TimeSpan Duration, DateTimeOffset LastUpdatedTime, bool IsPlaying, double Rate)` beside `IPlaybackSource`. Change `PositionChanged` to `Action<PlaybackSnapshot>` and `TrackChanged` to `Action<long, Track?>` so resets and positions share a generation. Produce `PlaybackClock.Sync(PlaybackSnapshot snapshot)`; retain `Position`, `Reset`, and the injectable `ITimeSource`. Source generation changes on track replacement/session loss/reconnection; sequence increases per observation. App derives duration/state updates from each snapshot and its generation's retained Track, rather than reading a possibly newer Current after dispatch.

- [ ] Add failing cases with fake time: `Seek_backwards_reanchors` (80s→20s yields 20s), `Seek_forwards_reanchors` (20s→80s yields 80s), `Repeated_snapshot_does_not_rewind` (20s + 1s elapsed stays 21s on repeated raw snapshot), `Changed_position_with_same_timestamp_is_accepted` (80→20 accepted), `Older_sequence_is_ignored`, and `Pause_resume_excludes_paused_time` (10s plus 30s pause remains 10s).
- [ ] Run `dotnet test -c Release --filter FullyQualifiedName~Playback`; require the new assertions to fail for the existing defect, then implement.
- [ ] Store the last accepted raw snapshot separately from the interpolated anchor. Ignore older sequence/generation observations; treat changes in position/update-time/state/rate as fresh, with changed positions accepted in either direction. Deduplicate unchanged polls. Freeze on pause and interpolate via Stopwatch only while playing. Apply Task 1's measured timestamp policy; guard invalid rate and negative/out-of-range positions. Do not keep an unconditional monotonic position clamp.
- [ ] Distinguish observation order from provider freshness: a higher Sequence only means a later poll, not fresher media data. Add `Provider_timestamp_regression_is_not_a_seek` and `State_only_pause_preserves_interpolated_position` (raw=20s, interpolation=21s, pause with unchanged raw timestamp/position freezes at 21s). A paused seek with changed position must still reanchor. Timestamp regression or state-only changes must not blindly reset to stale raw position. Task 1 must settle any provider-specific timestamp-reset exception.
- [ ] Include `TimeSpan ObservedAt` in PlaybackSnapshot, measured using the same injected monotonic time source as PlaybackClock, and pass that source to SmtcPlaybackSource. Compensate elapsed time between observation and UI application only while playing. Test delayed dispatcher delivery and duration zero (unknown, never clamp position to zero). The consumer sets generation on TrackChanged before accepting snapshots; clock Reset must not allow an old generation to become current again.
- [ ] Serialize session reads, clear `_lastIdentity` on loss, and raise track reset for same-song reconnection. Update duration/state without falsely resetting the clock. Capture generation before awaited media-property reads and discard superseded results.
- [ ] Adapt App wiring in this task so the solution builds. Keep polling at 250ms. Events are optional only if Task 1 proves a useful latency benefit; if added, detach handlers on session replacement/disposal.
- [ ] Run the focused tests and `dotnet test -c Release`; require all to pass. Replace the old test that demands all backward updates be ignored with the fresh-versus-stale tests above.
- [ ] Review the scoped diff and commit as `fix: follow fresh playback positions in both directions` if commits are authorized.

## Task 3: Represent timing truthfully and parse LRC safely

**Files:** modify `Core/LyricsDoc.cs`, `Core/TtmlParser.cs`, `Core/LyricsResolver.cs`; create `Core/LrcParser.cs`; extend `TtmlParserTests.cs`, add `LrcParserTests.cs`.

**Interfaces:** add `LyricsTiming { None, Line }` and an init-only `LyricsDoc.Timing` defaulting to Line for compatibility. Retain existing positional record constructors. Produce `LrcParser.Parse(string lyricsId, string lrc, TimeSpan duration) -> LyricsDoc`; unusable input throws FormatException. For TTML, untimed text is represented with Timing=None, never interpreted as real zero-time lines by consumers.

- [ ] Test TTML `timing=None` with text yields Timing=None; wholly missing timestamps also yields None. Timed paragraphs with non-finite/overflow values throw FormatException; empty/malformed documents remain safe misses at the resolver boundary.
- [ ] Test synthetic LRC `[00:10.00][00:20.00]Example` creates starts at 10s and 20s. Positive LRC offset advances display: `[offset:500]` moves the 10s cue to 9.5s; negative resulting timestamps clamp to zero. This is interpretation of embedded source metadata, not a user-adjustable offset. Reject conflicting offset tags.
- [ ] Test metadata tags are ignored, unordered valid entries are sorted, missing end times use the next start/final duration, malformed timestamps are rejected, and times exceeding duration+2s are rejected. Plain text does not pass as timed LRC. Empty spacer entries must not create fake sung lines.
- [ ] For mixed timed/untimed TTML, ignore whitespace-only spacers but never assign zero starts to nonempty untimed paragraphs. Treat unsupported mixed timing as unusable synchronized content (None), while malformed explicit timestamps throw FormatException. Add both cases. Clamp inferred final end to at least its own start when an accepted cue lies inside the duration+2s tolerance; every generated line must satisfy End >= Begin.
- [ ] Run `dotnet test -c Release --filter "FullyQualifiedName~TtmlParser|FullyQualifiedName~LrcParser"`, observe red, implement using existing XML and standard regex/invariant numeric parsing, then require green.
- [ ] Keep timing parsing bounded and finite; normalize parsing failures into FormatException. Avoid wholesale copying of Lyrica's parser or fabricated four-second timings.
- [ ] Run full tests and review/commit as `fix: distinguish untimed lyrics and validate timestamp input`.

## Task 4: Direct LRCLIB retrieval with recording validation

**Files:** create `Core/LrclibClient.cs`, `tests/APMLyrics.Tests/LrclibClientTests.cs`; modify `Core/LyricsMatcher.cs` only for reusable recording-safe normalization.

**Interfaces:** define `LyricsResolutionKind { Loading, Synced, Unsynced, NotFound, NetworkUnavailable, PendingDuration }` and `LyricsResolution(LyricsResolutionKind Kind, LyricsDoc? Document = null, string? Source = null)` in `LyricsDoc.cs`. Define `LrclibCandidate(long Id, string Title, string Artist, string Album, TimeSpan Duration, string? SyncedLyrics, string? PlainLyrics)` in LrclibClient.cs. Produce `LrclibClient(HttpClient client)` and `Task<LyricsResolution> ResolveAsync(Track track, CancellationToken ct)`. Use HttpMessageHandler fakes, not live HTTP, in tests.

- [ ] Tests: synthetic same-title candidates 281/30/2/139s for a 139s track select 139s; unknown duration gives PendingDuration; delta=2s accepted and >2s rejected. Wrong artist/title/live qualifier is rejected even with identical duration.
- [ ] Tests: agreeing known album beats conflicting album; missing album does not invent agreement. Equally ranked differing timed payloads return NotFound; equivalent duplicates collapse. Valid matching plain-only data returns Unsynced, not Synced.
- [ ] Run `dotnet test -c Release --filter FullyQualifiedName~LrclibClient`; observe failures then implement direct `https://lrclib.net/api/search` using encoded track_name/artist_name, a descriptive app User-Agent, and response metadata. Search responses already contain lyrics; avoid redundant get calls unless needed to obtain missing payload.
- [ ] Fetch current LRCLIB docs at execution time. Bound overall call duration to 10 seconds, reject oversized responses above 2 MiB, treat 429/5xx/timeout as NetworkUnavailable, and honor Retry-After within the existing deadline with at most one retry. Propagate caller cancellation distinctly. Do not retry 404/empty success as network failure.
- [ ] Implement selection: safe normalized exact title/artist, qualifier agreement, known duration within tolerance, album agreement then smallest duration gap, equivalent-content deduplication, otherwise reject unresolved ties. Never remove `live` when forming fallback identity. Parse winner with Task 3; malformed synchronized content is not accepted merely because syncedLyrics is nonempty.
- [ ] Validate identity first, then parse all identity-eligible synchronized candidates before ranking usable ones. A plain-only or malformed first candidate must not hide a valid timed candidate. Determine Unsynced only if no valid timed candidate survives and confidently matching plain text exists. Compare parsed normalized text/start/end sequences for equivalent duplicates, not raw LRC strings. Add `Plain_first_does_not_hide_synced`, `Malformed_first_does_not_hide_synced`, and equivalent-LRC-formatting tests.
- [ ] Add tests for URL encoding, 503/429/cancellation, oversized payload, malformed JSON and null metadata; run focused and full suite, review/commit as `feat: fetch validated synced lyrics directly from LRCLIB`.

## Task 5: Persist accepted fallback results without poisoned misses

**Files:** create `Core/FallbackLyricsCache.cs`, `tests/APMLyrics.Tests/FallbackLyricsCacheTests.cs`.

**Interfaces:** `FallbackLyricsCache(string directory)`, `LyricsDoc? Read(Track track)`, `void Write(Track track, LrclibCandidate candidate, LyricsDoc document)`. Default directory `%APPDATA%\APMLyrics\lyrics-cache`. Update LrclibClient constructor with optional cache parameter and invoke it only after candidate validation. Cache schema records provider/id, requested recording metadata, candidate metadata/duration, timing kind, and timed lines.

- [ ] Tests: accepted synced result round-trips; another recording/version cannot reuse it; unknown duration does not reuse it; corrupt/truncated/unsupported-schema JSON returns null; injected write failure does not destroy an existing entry. Network failures never produce persistent negative entries.
- [ ] Run the new test class to observe red. Use a SHA-256 filename of normalized metadata including duration (no raw title in filenames), validate metadata and timestamps again on read, write to a same-directory temporary file and atomically replace/move. Serialize writers or use unique temp names; failed persistence must not discard an already valid in-memory result.
- [ ] Cache identity uses the requested Track's stable metadata and whole-second rounded duration, not the candidate's duration or transient IsPlaying flag. Store the exact requested and candidate durations for revalidation. Identical input must produce identical keys across process restarts. Test pause/resume key stability. Reads happen before network search; “only after candidate validation” applies to writes, not reads. Cache write exceptions never change a successful resolution to NetworkUnavailable.
- [ ] Preserve original provider identity and metadata. Use schema version 1. No database, cache eviction framework, or permanent negative cache.
- [ ] Add integration test: first resolution succeeds online and caches; second uses failing HTTP but returns the cached document without requesting the provider. Run focused/full suite and review/commit as `feat: cache validated fallback lyrics for offline reuse`.

## Task 6: Coordinate local retries, fallback, and stale-result rejection

**Files:** modify `Core/LyricsResolver.cs`, `Core/LyricsMatcher.cs`, `Core/CatalogClient.cs`, `App.xaml.cs`; create `Core/LyricsCoordinator.cs`, `tests/APMLyrics.Tests/LyricsCoordinatorTests.cs`; extend existing resolver tests.

**Interfaces:** add `bool TryGetCached(string lyricsId, out TrackInfo? info)` to BOTH ICatalogClient and CatalogClient and update test fakes (true/null means cached negative; false means unknown). Produce `LyricsResolver.ResolveCached(Track track, DateTimeOffset? trackStart = null) -> LyricsResolution` with no HTTP, plus `ResolveLocalAsync(Track track, CancellationToken ct, DateTimeOffset? trackStart = null) -> Task<LyricsResolution>` for bounded catalog enrichment. Keep a thin legacy ResolveAsync wrapper only while callers migrate. Coordinator constructor takes `Func<Track, DateTimeOffset, LyricsResolution> localCached`, `Func<Track, DateTimeOffset, CancellationToken, Task<LyricsResolution>> localEnrich`, `Func<Track, CancellationToken, Task<LyricsResolution>> remote`, optional `Func<TimeSpan, CancellationToken, Task> delay`, and optional `Func<DateTimeOffset> utcNow`. Expose `Start(Track track, long generation)`, `UpdateTrack(Track track, long generation)`, `NotifyCacheChanged()`, `Stop()`, and `event Action<long, LyricsResolution> Changed`. Implement IDisposable. Delegates avoid an interface/factory framework for single implementations.

- [ ] Tests with controllable tasks/delays: local miss starts remote promptly; local synced hit avoids remote; untimed associated local hit allows remote; unrelated static file never sets Unsynced; duration zero→known retries once; stale A completion after B cannot emit B content; Stop cancels everything; cache burst coalesces; accepted synced content survives later transient errors.
- [ ] Run resolver/coordinator focused tests red. Refactor local resolution to inspect cached metadata first and skip network lookup for clearly incompatible known-duration files. Do not sequentially await every historic catalog ID before remote fallback can start. Run remaining local enrichment concurrently with fallback, bounded to one local resolution at a time; cancel superseded work.
- [ ] Run cache scanning/parsing off the UI thread. Await only ResolveCached before deciding whether to launch remote; after a cached miss, run localEnrich and remote independently. Bound local enrichment to a 10-second deadline and coalesce additional enrichment triggers into at most one pending rescan. Catalog lookup/search must propagate caller cancellation rather than swallowing it in broad exception handlers. Add `Hung_catalog_does_not_delay_remote` and cancellation-propagation tests.
- [ ] Preserve conservative identity requirements. An unknown duration must not be treated as zero-length. Exact verified identity may establish plain content; recent-file timing alone is insufficient to assign untimed lyrics. Keep uncertainty as NotFound.
- [ ] Coordinator owns one cancellation lifetime per generation, absolute 1/3/8-second local retries, and coalesced cache events. Deduplicate in-flight remote requests for the same metadata; only start remote after duration is known. Prefer late validated Apple synced results without resetting clock. Network cooldown applies across cache events, not just inside one HTTP call.
- [ ] Make state precedence deterministic: accepted Apple synced > accepted fallback synced > associated Unsynced > network failure > completed miss. Emit Loading initially; show PendingDuration as loading until the 8-second local retry ends, then a completed-miss status if no association exists, while remaining eligible for a later duration update. Never downgrade accepted synced content within a generation. Publish misses only after the relevant concurrent attempts complete, not when the first source misses. Test both completion orders and static-local plus remote outage.
- [ ] Remote policy: one logical attempt per recording metadata per generation, with Task 4's single bounded HTTP retry. On NetworkUnavailable, schedule at most one further logical attempt after 30 seconds; retain a per-recording 30-second cooldown across rapid away/back track changes. Completed misses do not retry remotely during the same generation. Cache notifications trigger local work only. A materially changed known duration (>2s from the validated duration) cancels the old attempt, invalidates mismatching content, and permits revalidation/new resolution. Test 503→success at 30s, burst notifications, and duration correction after initial acceptance. Keep cooldown bookkeeping in memory only.
- [ ] App subscribes once, marshals events to dispatcher, checks generation again inside dispatcher callback, and cancels on null track/exit. Duration metadata updates call UpdateTrack without resetting song position. Dispose cancellation sources/watchers/coordinator cleanly.
- [ ] Run focused and full tests; review/commit as `fix: coordinate lyric retries and discard obsolete track results`.

## Task 7: Status rendering and end-to-end acceptance

**Files:** modify `Ui/OverlayWindow.xaml.cs`, `Ui/LyricPanel.xaml.cs`, `App.xaml.cs`, `README.md`, `SECURITY.md`, `docs/manual-testing.md`; add focused status tests to the existing test project only if extracting the status mapping into a pure internal function is useful.

**Interfaces:** `OverlayWindow.Apply(LyricsResolution resolution)` maps result to content/status; extend LyricPanel.Show with optional `string? status = null` preserving settings preview calls. Explicitly prevent Timing=None from entering synchronized rendering.

- [ ] Verify exact mapping: resolving→`Finding synced lyrics…`; known static-only→`Only unsynced lyrics available`; completed miss→`No matching synced lyrics found`; unavailable network without a usable result→`Couldn't check online lyrics`. Show song title above status. Keep Nothing playing for no session.
- [ ] Cover status precedence with pure coordinator tests from Task 6; manually verify renderer text. Add a regression for a new document arriving after a seek: call Panel.Tick with the current corrected position before/alongside showing the document so the previous track's cached panel position cannot render the wrong initial line.
- [ ] Keep line selection tied to corrected PlaybackClock; never reset position on lyrics arriving or switching providers. No scrolling, offset control, provider settings page, or redesign.
- [ ] Run `dotnet test -c Release` and `dotnet build -c Release`; require zero failures. Inspect final diff for accidentally retained old forward-only assumptions and bypasses of generation checks.
- [ ] Complete Task 1's live capture if pending. Manually verify timed Apple track, Pasti Ada Jalan fallback, forward/backward seeks, pause-then-seek, rapid skips, same-song reconnect, offline warm cache, and static-only status. Record observed correction latency and vocal alignment; passing unit tests alone does not satisfy this gate.
- [ ] Update README and SECURITY to accurately describe LRCLIB metadata transmission, Apple/GitHub requests, local lyric caching, and failure behavior. Record remaining unverified cases honestly; do not claim all tracks align or that private Apple downloading was implemented.
- [ ] Review/commit as `feat: surface lyric availability states and document fallback behavior`. Do not tag, publish, or bump release versions without a separate user request.

## Handoff and completion

### Plan review — 2026-10-21

Rechecked against the approved spec and existing resolver/catalog/clock interfaces. Corrected missing loading state, provider-freshness versus poll-sequence distinction, pause-only rewind risk, missing observation time, interface/fake compatibility, local-first latency conflict, malformed/untimed candidate masking, cache read/write ambiguity, and retry/status precedence. These are execution clarifications within the approved scope. Live provider timestamp behavior and vocal alignment remain measured acceptance gates, not claims established by document review.

Read the spec and research ledger alongside this plan. Execute tasks sequentially except Task 1's blocked live measurements may coexist with independent Tasks 3–5. Do not consider playback synchronization complete until live validation is recorded. Private Apple endpoints, audio analysis, and native hooking remain out of scope for implementation.

Suggested next-model instruction: “Implement docs/superpowers/plans/2026-10-20-lyrics-reliability.md inline using executing-plans. Read its approved spec and research ledger first. Keep diffs minimal, use existing dependencies, and stop to request the live playback actions when needed. Do not publish a release.”
