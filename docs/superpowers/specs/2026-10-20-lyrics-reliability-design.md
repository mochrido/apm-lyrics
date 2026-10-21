# Reliable lyric acquisition and seeking

Status: approved by the user; implementation deliberately deferred to the user's next model.

## Agreed outcome

Standalone Windows/.NET app. Prefer correctly identified timed Apple cache lyrics. Automatically fetch synchronized fallback lyrics when Apple lyrics are absent or untimed. Fallback must match the playing recording. Follow pause/resume and seeks in either direction. No manual offset, scrollable plain lyrics, invented timestamps, or timestamp stretching. When only confidently identified static lyrics exist, display `Only unsynced lyrics available` beneath the title.

## Evidence and limits

See `docs/apple-music-lyrics-investigation.md`. Apple exposes untimed TTML as well as timed TTML. Current clock refuses backward updates. Current cache matching cannot associate every lyric ID with a catalog track. Private Apple downloading is not verified.

Follow-up LRCLIB search returned HTTP 200 for Pasti Ada Jalan / Perunggu. Results included durations 281, 30, 2, 139, and 139 seconds; the actual SMTC duration was 139 seconds (iTunes 139.480). IDs 17312337 (album Pasti Ada Jalan) and 38916161 (album 2:20) both had 31 ordered timing entries from 0.02 to 137.25 seconds. This verifies candidate availability, not vocal alignment. The earlier request returned 503. Apple Music was closed at the follow-up, so seek traces and listening verification remain outstanding.

Microsoft documents LastUpdatedTime as the UTC time the timeline properties were updated and exposes TimelinePropertiesChanged. Those docs do not establish Apple's update cadence or guarantee seek-event delivery:
- https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties.lastupdatedtime
- https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession.timelinepropertieschanged

## Proposed acquisition behavior (engineering choices)

Use existing HttpClient/JSON/XML facilities and add a small direct LRCLIB client plus LRC parsing; no Python backend, aggregation framework, or new production package.

Resolution order: validated Apple timed document, validated locally cached fallback, network LRCLIB candidate. Retain a known plain-lyrics result only to select the final status, never render it as timed. Network fetching starts promptly after a local miss; do not block behind metadata lookups for every historic cache file. Coalesce cache notifications, and retry local misses after approximately 1, 3, and 8 seconds. Retry once when unknown duration becomes known. These are proposed bounded defaults, not measured optimal timings.

Each song/session generation owns cancellation and results. Track change or session loss cancels ALL pending work and clears old content. Every result is generation-checked at UI application time. Same-song reconnect starts a new generation. Late valid Apple lyrics may replace a fallback without resetting playback position.

### Matching and trust boundaries

- Normalize case, Unicode/whitespace, and Apple's artist/album separator. Preserve recording qualifiers such as live, remix, acoustic, remaster, and edit; existing normalization that removes `live` must not define fallback identity.
- Require title and artist agreement. Use album when reliably available, including cautiously extracted Apple suffix metadata. Explicit recording-version conflict rejects a candidate.
- Require known playback duration and at most 2 seconds difference for online acceptance initially. Wait for duration rather than accepting a speculative online match.
- Inspect all search candidates, not result zero. Prefer agreeing album and smallest duration difference. Equivalent lyric/timing payload duplicates may be collapsed; otherwise unresolved equal-confidence candidates are rejected.
- Validate nonempty timed content, finite nonnegative positions, and timestamps within track duration plus the 2-second tolerance. Preserve repeated LRC timestamp tags and offsets defined in the file; malformed or contradictory timing is a miss, not a crash. Sort valid LRC entries if needed; do not invent synchronization for untimed lines. Source line endings may be inferred from the next start for display, but inferred ends are not evidence of vocal timing.
- Cache accepted results by normalized recording metadata and duration, retaining provider ID and original metadata. Atomic writes; bad cache entries are ignored. Do not permanently cache HTTP failures. Respect Retry-After and bounded timeout/cancellation; avoid request storms.
- Never claim an unrelated untimed cache file belongs to the current song. If identity cannot be established, it cannot justify the static-lyrics status.

### User-visible states

Title plus `Finding synced lyrics…` while resolving. On successful matching, normal synchronized rendering. After resolution: `Only unsynced lyrics available` for known plain content; `No matching synced lyrics found` for completed misses; `Couldn't check online lyrics` for network failure when no local usable result exists. Keep already accepted lyrics during transient failures for the same generation.

## Proposed playback correction

Keep 250ms polling as the reliability baseline. Capture session identity, track identity, position, timeline update timestamp, duration, playback state/rate, and monotonic observation time together. Serialize observations so overlapping async reads cannot reorder results. Reset identity on session loss and resubscribe if events are used.

Replace the unconditional forward-only clamp with freshness-aware re-anchoring. A genuinely new authoritative position can move backward or forward. Identical repeated snapshots must not repeatedly rewind an interpolating clock. Old observations from a prior generation are discarded. Changed position must not be ignored merely because a provider repeats its timestamp. Pause freezes position; resume reanchors without accumulating paused time.

Use monotonic elapsed time for interpolation. Do not blindly extrapolate from arbitrarily old/future LastUpdatedTime values. Before choosing timestamp-age compensation or a discontinuity threshold, measure Apple's reporting during normal playback and seeks; record that decision in the implementation handoff. Polling-only may be sufficient; add timeline events only if measurements justify their complexity. No manual calibration or audio-derived alignment is included.

Proposed target: visible correction within 500ms after Windows exposes the new position. This does not promise 500ms from the user's gesture when Apple/Windows delays reporting.

## Verification and execution boundary

Use the existing xUnit suite. Add focused cases for backward and forward seeks, duplicate/stale observations, pause/resume, reconnect with identical title, rapid track switch cancellation, late duration, untimed TTML classification, malformed timing, wrong-version/wrong-duration LRCLIB results, duplicates/ambiguity, 503 recovery, and cached offline reuse. Use synthetic fixtures, not committed full copyrighted lyric responses.

Manual verification must cover Pasti Ada Jalan's validated LRCLIB candidate, an Apple-timed track, repeated forward/backward seeks, pause then seek, fast skips, offline startup with warm cache, and static-only/no-match states. Record Windows timeline observations and listening results before claiming synchronization works.

Private Apple API integration and deeper native decompilation remain separate research leads, not prerequisites for this first change. No product code has been changed. After user approval, write a file-by-file implementation plan for the execution model; do not begin implementation in this research session.
