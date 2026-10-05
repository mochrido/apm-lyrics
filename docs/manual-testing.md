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
