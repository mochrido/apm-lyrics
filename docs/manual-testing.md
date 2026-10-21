# APM Lyrics manual checklist

Run once per release on a real Apple Music session. Record the date and result.

- [ ] Overlay appears on launch and shows the current line.
- [ ] Drag from the centre of the overlay: it moves, and the position persists after restart.
- [ ] Resize from all four edges and all four corners: the text refits to the new size, and the overlay can be shrunk to about a taskbar-height strip.
- [ ] Grow the overlay to roughly double its default size: the lyric text scales up with it instead of staying at its old size.
- [ ] Shrink the overlay to its minimum: the text is still readable and nothing is clipped.
- [ ] No progress bar is drawn under the current line.
- [ ] Play a song with lyrics: the current line advances in time with the audio.
- [ ] Skip to the next track: the previous lines clear immediately, a loading state shows, then the new lines appear.
- [ ] Play a song with no lyrics: "No lyrics for this song" or the title state shows, never the previous song.
- [ ] Pause: the highlight stops advancing. Resume: it continues from the right place.
- [ ] Toggle click-through from the tray: clicks pass through the overlay to the window below.
- [ ] With click-through on, use the tray menu to turn it off: the overlay is clickable again.
- [ ] Open a maximized and a full-screen window: the overlay stays on top.
- [ ] Switch to single-line mode: the overlay shows one line at the configured size.
- [ ] With a song playing, open Settings from the tray and change the font family and the neighbour opacity: the overlay updates live as you change them, with no confirmation button to press.
- [ ] Close and relaunch: position, size, mode, and colours restore.
- [ ] If a second monitor at a different scale is available: the text is crisp on both.
- [ ] Quit from the tray: the process exits with no orphaned window.

## Lyrics reliability (2026-10-21) — manual QA checklist

- [ ] Play a song Apple Music shows with synced lyrics: the overlay advances with the audio.
- [ ] Seek backwards mid-song: within about half a second the highlighted line moves back to the correct line.
- [ ] Seek forwards mid-song: the highlighted line jumps forward and keeps tracking.
- [ ] Pause, seek while paused, then resume: the highlight is correct immediately after resume and does not drift by the paused duration.
- [ ] Skip rapidly across several tracks: lyrics never show for the wrong song and never sit on the previous song's lines.
- [ ] Play "Pasti Ada Jalan" (Perunggu): Apple's own lyrics are static; the overlay says `Only unsynced lyrics available` rather than scrolling text.
- [ ] Play a song with a validated online fallback: lyrics advance in time with the vocals.
- [ ] Play a song with no lyrics anywhere: overlay says `No matching synced lyrics found`.
- [ ] Disconnect the network and replay a previously resolved fallback song: lyrics still appear from cache.
- [ ] With Apple Music closed, the overlay shows `Nothing playing`.
- [ ] Restart Apple Music and play the same song again: lyrics reappear (same-song reconnect).
- [ ] Record a timeline trace with `scripts/Observe-AppleMusicTimeline.ps1` during the seek tests and note observed correction latency.

## UI/UX overhaul (2026-10-19) — manual QA checklist

- [ ] Overlay: text centered horizontally AND vertically in both modes.
- [ ] Single-line mode: text sits in the vertical middle, not hugging the top.
- [ ] Multi-line: consistent spacing between prev/current/next; current bright and larger.
- [ ] Resize small: neighbours disappear (radius sheds) before text gets tiny; text never below ~14px in auto-fit.
- [ ] Resize large: text grows with the window (never "stays small").
- [ ] Settings → Lock font size ON: text stays at the chosen size through resize (clips/wraps instead).
- [ ] Lock toggle ON seeds the slider from the current size; label reads "Font size (locked)".
- [ ] Drag overlay onto the taskbar: it lifts to sit above the taskbar, never buried.
- [ ] Secondary monitor below the primary: dragging there is not yanked back.
- [ ] Tray icon, Settings window, taskbar entry, and installer show the ♪A logo; legible at 16px.
- [ ] Settings: sidebar pages (Appearance / Colors / Behaviour / About) all reachable; live preview updates on every edit.
- [ ] Old settings.json (pre-lock fields): loads fine, lock off, locked size 28.
- [ ] Crossfade still plays on line change only, not on restyle.
- [ ] Click-through on: overlay ignores mouse; tray menu still recovers it.
