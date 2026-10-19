# APM Lyrics — UI/UX Overhaul Design

Date: 2026-10-19
Status: approved in brainstorming, pending builder check

## Intent and why

The overlay works mechanically but reads badly. Three real-user complaints drive this
project:

1. Text becomes unreadable when the window is resized small, and sometimes stays small
   when the window is enlarged (font-fitting math is fragile and has no readability floor).
2. In single-line mode the text hugs the top of the window instead of being vertically
   centered; in multi-line mode the spacing between lines is inconsistent (three separate
   TextBlocks with ad-hoc sizing).
3. When dragged over the Windows taskbar, the overlay sinks below it and becomes hard to
   retrieve.

The app also ships with no logo, and the Settings window is a plain single-column form.
The user wants a visual overhaul of both windows plus the tray/installer identity, not a
patch per symptom.

## Goals

- Overlay text is comfortably readable at every window size the user can produce by
  dragging/resizing.
- Overlay layout is centered (horizontal and vertical), with consistent line spacing in
  both multi-line and single-line modes.
- The overlay always renders above the taskbar and can never be lost under it by dragging.
- A real app logo ("♪A") used consistently: tray icon, Settings window icon, installer.
- Settings window redesigned as a categorized sidebar layout with live preview.

## Non-goals

- No changes to the lyrics pipeline (SMTC polling, cache scanning, matching, parsing) —
  it is well-tested and out of scope.
- No new lyrics features (translations, romanization, etc.).
- No theming/skinning system beyond the existing color settings.

## Anti-goals

- Animated backgrounds, progress bars, or anything that makes the overlay feel busy —
  the design direction chosen was calm, minimal, "floating text".
- A font so small it technically renders but cannot be read. Readability beats fit.

## Decisions from the brainstorming (user-chosen)

1. **Overlay layout: centered.** All lyric lines (prev/current/next) are center-aligned
   horizontally and vertically centered in the window. Current line bright and larger;
   neighbour lines dim and smaller. Consistent inter-line spacing replaces the current
   three-independent-TextBlocks layout.
2. **Single-line mode: always vertically centered**, whatever the window height.
3. **Font sizing: hybrid.** (Units: WPF DIPs throughout, matching the existing codebase
   and the 10–96 slider range.)
   - Default is auto-fit: font scales with window size, BUT with a readability floor of
     approximately 14 DIPs — in auto-fit the font never goes below it. When the window is
     too small for all rows at the floor, neighbour lines are hidden first (shrink the
     radius one step at a time), and only then does the current-line font shrink toward
     the floor; it stops at the floor and clips rather than becoming unreadable.
   - A "Lock font size" toggle in Settings switches to manual sizing: the user picks a
     size (10–96 DIPs, no floor when locked — a deliberate user choice is respected),
     resizing never changes it; the window is purely a viewport (text wraps or clips).
   - The font slider is repurposed when locked: one slider whose label changes to
     "Font size (locked)" and whose current value becomes the initial manual size; no
     second control.
   - The "resized larger but text stayed small" path already refits on
     `OnRenderSizeChanged` (commit 0ba521e); the rebuild keeps that and covers it with
     regression tests.
4. **Settings window: sidebar categories** in the style of modern Windows Settings:
   left nav with Tampilan (Appearance) / Warna (Colors) / Perilaku (Behaviour) /
   Tentang (About). A live lyric preview (styled like the real overlay) appears in the
   Appearance and Colors pages and updates instantly on every edit.
   (UI copy language: keep English in the UI as the app is today; category names above
   are the Indonesian working names used in discussion — the builder renders them in
   English: Appearance / Colors / Behaviour / About. — builder's call to confirm with
   user if ambiguous.)
5. **Taskbar fix — clamp, not z-order battle.** On Windows 11 the taskbar's band
   typically beats even topmost windows, so chasing "render above the taskbar" is
   fragile. Acceptance: **the overlay never ends up buried in the taskbar area**. On
   drag end (after `DragMove()` returns, and on `LocationChanged`), the window position
   is clamped so its bottom edge stays above the work-area bottom (i.e. on top of the
   taskbar, never inside it). Topmost is still applied correctly, but the clamp is the
   guarantee. This deterministically fixes "dragged to the taskbar and became hard to
   retrieve".
6. **Logo: "♪A"** — rounded-rectangle tile in Apple Music red (#fa2d48), white "♪A"
   centered. Deliverables: tray icon (H.NotifyIcon), Settings window/taskbar icon
   (.ico with 16/32/48/256 sizes), installer branding (Inno Setup), README image.
   The glyph pair must remain legible at 16px.

## Constraints

- WPF on .NET 8 (`net8.0-windows10.0.19041.0`), single project, no new major
  dependencies without reason (H.NotifyIcon.Wpf stays).
- Existing seams must survive: `LineWindow` pure math is extended or replaced but stays
  pure and unit-tested; `OverlayWindow` remains the only WPF surface.
- Settings persist via the existing `AppSettingsStore` debounce mechanism; new fields
  (`LockFontSize`, manual font size) get sensible defaults so old settings files load
  unchanged (missing fields fall back to defaults, as `AppSettings` already does).
- Click-through behaviour and the tray menu as the recovery path stay exactly as they
  are.

## Design details

### Layout (overlay)

Replace the fixed Prev/Curr/Next TextBlock trio with a centered vertical `StackPanel`
(or `UniformGrid`) inside a vertically-centered container:
- Multi-line: current line TextBlock (weight ~600, full brightness), neighbour TextBlocks
  (~70% size, dimmed by `NeighbourOpacity`, consistent fixed spacing — e.g. a shared
  `Margin` scaled with font size).
- Single-line: one TextBlock inside a container with `VerticalAlignment="Center"` and
  `TextAlignment="Center"`.
- All text `TextAlignment="Center"`.

### Font fitting

`LineWindow.FitFontSize` is rewritten:
- Inputs: base font size, width, height, rows, lock flag, manual size.
- Auto-fit: compute scale as today (tighter axis wins), apply, then clamp to
  [ReadabilityFloor (~14), MaxFontSize]. If the clamped size cannot fit `rows` lines in
  the available height, reduce rows (hide neighbours) before shrinking below the floor.
- Locked: return the manual size; the caller wraps/clips.
- The overlay re-runs fitting on `SizeChanged` (this fixes the "stays small when
  enlarged" bug), and on line-count changes.

### Taskbar z-order

Investigate in this order (builder's implementation latitude):
1. Ensure `ShowActivated="False"`, `Topmost="True"` and re-assert topmost with
   `SetWindowPos(hwnd, HWND_TOPMOST, …, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE)` after
   drag end (`LocationChanged`/`DragMove` return).
2. If the taskbar still wins (some shells keep an above-topmost band), fall back to
   clamping the drop position to the work area on drag end so the window can never rest
   fully inside the taskbar region while remaining reachable.
3. Keep the existing startup virtual-screen clamp; extend it with the drag-end clamp.

### Settings window

- Window gets the app icon; fixed min width (~480px); nav `ListBox` on the left, content
  `Frame`/panels on the right; `NavigationRail`-like styling in plain WPF (no new libs).
- Pages: Appearance (mode, font family, neighbour radius, lock font size + manual size),
  Colors (current/neighbour/backdrop colors, dim toggle, opacity), Behaviour
  (click-through default, taskbar-always-on-top note), About (version, logo, links).
- Live preview panel renders the same layout the overlay uses (shared style/control if
  cheap, otherwise a faithful mini mock) and updates on every keystroke/slider tick.
- Settings changes continue to flow through the single `SettingsChanged` funnel and the
  debounced save.

### Logo

- Author as SVG (source of truth), export `.ico` (16/32/48/256) for the app and
  installer, plus PNG 256 for README. H.NotifyIcon tray icon points at the ico.
- Red #fa2d48 rounded tile (corner radius ≈ 22% of size), white "♪A" centered, glyph
  pair chosen to stay legible at 16px (may use drawn note glyph rather than the ♪
  codepoint if the font renders it inconsistently at small sizes — builder's call).

## Builder-check calls already made (my calls, check them)

- Neighbour shedding requires restructuring Prev/Next from two joined-`\n` TextBlocks
  into per-line TextBlocks (or reworked range logic) — acknowledged as a layout rewrite,
  not a tweak.
- Live preview uses a shared lyric UserControl extracted from OverlayWindow's visual
  tree; "only WPF surface" constraint read as "only window".
- Tray icon switches from `SystemIcons.Application` to the new .ico via WPF Resource;
  csproj gains `ApplicationIcon`; exe/window icon wiring is new.
- New settings fields `LockFontSize` (default false) and locked size (default 28) added
  to `AppSettings` + `Sanitize`.
- The 180ms crossfade survives the rewrite (applied on line change only, as today).
- Settings window min width: 480.
- Drop-clamp math extracted to a pure, testable helper.

## What is left to the builder

- Exact XAML structure of the overlay panels and Settings pages (within the decided
  layout direction).
- The readability floor constant (target ~14px; verify against real 100% and 150% DPI).
- Whether the taskbar fix needs only the topmost re-assert or also the drop clamp —
  implement the clamp regardless as a safety net, but measure the z-order first.
- Whether neighbour hiding in auto-fit drops to single-line or to fewer neighbour rows
  (prefer: shrink radius one step at a time).
- Test coverage: extend `LineWindowTests` for the new fitting rules (floor, lock,
  neighbour shedding); a UI-logic test for the drop-clamp math.

## Out of scope / later

- Localization of UI copy (currently English).
- Multiple monitor per-monitor DPI edge cases beyond what WPF gives for free.
