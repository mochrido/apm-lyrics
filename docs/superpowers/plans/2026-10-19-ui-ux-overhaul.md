# APM Lyrics UI/UX Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the overlay's layout and font-sizing (centered, readable, hybrid lock), fix the taskbar-drag bug, redesign Settings as a sidebar-categorized window with live preview, and add the "♪A" app logo everywhere.

**Architecture:** Pure windowing math (`LineWindow`, new `DropClamp`) is extended and unit-tested first; the overlay visual tree is rewritten around a new shared `LyricPanel` UserControl (used by both the overlay and the Settings preview); Settings is rebuilt as a nav-list + content pages window; the logo lands as assets wired into csproj, tray, and installer.

**Tech Stack:** WPF on .NET 8 (`net8.0-windows10.0.19041.0`), xUnit, H.NotifyIcon.Wpf 2.1.4, Inno Setup.

**Spec:** `docs/superpowers/specs/2026-10-19-ui-ux-overhaul-design.md`

## Global Constraints

- Units are WPF DIPs everywhere; font slider range stays 10–96 DIPs.
- Auto-fit readability floor ≈ 14 DIPs; no floor when the user locks the font size.
- UI copy stays in English.
- No new NuGet dependencies.
- Existing settings JSON must load unchanged (new fields fall back to defaults).
- Click-through behaviour and the tray menu recovery path stay as they are.
- The 180 ms crossfade on line change survives (current line only, as today).
- Commit messages end with `Co-Authored-By: Claude Code <noreply@anthropic.com>`.
- Test command: `dotnet test tests/APMLyrics.Tests/APMLyrics.Tests.csproj` (from repo root also works).

## Review Focus

- Tiny window + lock ON: text must render at the user's size and clip/wrap, never auto-shrink.
- Tiny window + lock OFF + radius 4: neighbours must shed before the current line goes below 14 DIPs.
- Window enlarged: fitted size must grow (regression for the "stays small" bug) — recompute on every size change.
- Drag fully onto the taskbar: window must land clamped above the work-area bottom, on all monitors (negative coordinates).
- Old settings.json without `LockFontSize`: loads with lock off, size 28.
- 16px logo render: "♪A" must be legible (use a drawn note glyph if the ♪ codepoint renders thin).

---

### Task 1: Font fitting v2 in `LineWindow` (pure math)

**Files:**
- Modify: `src/APMLyrics/Ui/LineWindow.cs`
- Test: `tests/APMLyrics.Tests/LineWindowTests.cs`

**Interfaces:**
- Consumes: existing `LineWindow.DesignWidth/DesignHeight`, `Compute`, `CurrentIndex`.
- Produces: `public const double ReadabilityFloor = 14.0;` and
  `public static double FitFontSize(double baseFontSize, double width, double height, int rows, bool lockSize, double lockedSize)`
  — returns the effective font size. Locked: returns `lockedSize` clamped to [10, 96] (no floor). Auto-fit: current scale logic (tighter axis wins, `rowScale = rows > 3 ? 3.0/rows : 1.0`), clamped to `[ReadabilityFloor, MaxFontSize]`.
  Also produces: `public static int EffectiveRows(int radius, int lineCount, int currentIndex, double fittedSize, double width, double height)` — sheds neighbour rows (radius downward one step at a time, to 0) while the rows at the fitted size would exceed usable height; returns the effective radius's row count (`r*2+1`, capped by line count). Keep the old 4-arg `FitFontSize` as a thin wrapper delegating with `lockSize: false, lockedSize: 0` so existing call sites/tests still compile.

- [ ] **Step 1: Write failing tests**

Test names and assertions (add to `LineWindowTests`):
- `Locked_size_is_returned_verbatim` — `FitFontSize(28, 100, 50, 1, true, 40)` → 40.
- `Locked_size_clamps_to_10_96` — `FitFontSize(28, 100, 50, 1, true, 5)` → 10; same with 200 → 96.
- `Autofit_never_goes_below_floor` — `FitFontSize(28, 60, 24, 3, false, 0)` → 14.
- `Autofit_grows_with_window` — `FitFontSize(28, 1120, 320, 3, false, 0)` ≈ 2× `FitFontSize(28, 560, 160, 3, false, 0)` (assert ratio within 1%).
- `EffectiveRows_sheds_radius_before_shrinking_below_floor` — with width/height that fit only 1 row at floor, radius 2, lineCount 9, index 4 → `EffectiveRows` returns 1.
- `EffectiveRows_keeps_radius_when_space_available` — generous width/height, radius 2 → 5.

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter LineWindowTests`
Expected: FAIL (new overloads missing; wrapper tests may pass).

- [ ] **Step 3: Implement in `LineWindow.cs`**

Locked path first (trivial), then auto-fit clamp to `ReadabilityFloor`, then `EffectiveRows`: loop radius from configured down to 0, compute total height need as `fitted * line-height factor (use 1.3) * rows` vs `height - PaddingY`, first radius that fits wins. Height-per-row estimate: `fittedSize * 1.3` for the current row and `fittedSize * 0.7 * 1.3` for neighbour rows.

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test --filter LineWindowTests` — Expected: PASS.

- [ ] **Step 5: Commit**

`git add src/APMLyrics/Ui/LineWindow.cs tests/APMLyrics.Tests/LineWindowTests.cs && git commit -m "feat: hybrid font fitting with readability floor and neighbour shedding"`

---

### Task 2: `DropClamp` — taskbar drag clamp (pure math)

**Files:**
- Create: `src/APMLyrics/Ui/DropClamp.cs`
- Test: `tests/APMLyrics.Tests/DropClampTests.cs`

**Interfaces:**
- Produces: `public static class DropClamp { public static (double Left, double Top) Clamp(double left, double top, double width, double height, double workAreaBottom, double virtualBottom); }`
  — if the window's bottom (`top + height`) would cross into the taskbar (`> workAreaBottom`), move `top` so bottom sits at `workAreaBottom`; but if the window is taller than the work area, keep top at the virtual screen top so it never lands below the work area entirely. Never raises `top`. Windows below `workAreaBottom` already (secondary monitors) pass through unchanged.

- [ ] **Step 1: Write failing tests**

- `Window_fully_on_taskbar_is_lifted_above_workarea` — left 100, top 1040, w 560, h 160, workAreaBottom 1040, virtualBottom 1080 → top 880.
- `Window_already_above_workarea_unchanged` — top 900 → 900.
- `Taller_than_workarea_pins_to_top` — height 1100, workAreaBottom 1040 → top 0 (virtual top).
- `Secondary_monitor_below_workarea_passes_through` — top 1200, workAreaBottom 1040, virtualBottom 2160 → 1200.
- `Never_moves_up_when_partially_overlapping` — top 1000 (bottom 1160 > 1040) → 880.

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter DropClampTests` — Expected: FAIL (type missing).

- [ ] **Step 3: Implement `DropClamp.Clamp`**

Single if-chain per the Produces contract; ~10 lines.

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test --filter DropClampTests` — Expected: PASS.

- [ ] **Step 5: Commit**

`git add src/APMLyrics/Ui/DropClamp.cs tests/APMLyrics.Tests/DropClampTests.cs && git commit -m "feat: clamp overlay drop position out of the taskbar area"`

---

### Task 3: New settings fields

**Files:**
- Modify: `src/APMLyrics/Config/AppSettings.cs`
- Test: `tests/APMLyrics.Tests/AppSettingsTests.cs`

**Interfaces:**
- Produces: `AppSettings.LockFontSize` (`bool`, default `false`) and `AppSettings.LockedFontSize` (`double`, default `28`), both `init` properties; `Sanitize` clamps `LockedFontSize` to [10, 96].

- [ ] **Step 1: Write failing tests**

- `Old_settings_json_loads_with_lock_defaults` — serialize a JSON string without the new fields, `Load` from temp file → `LockFontSize` false, `LockedFontSize` 28.
- `Locked_size_sanitized` — JSON with `"LockedFontSize": 500` → clamps to 96.

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter AppSettingsTests` — Expected: FAIL.

- [ ] **Step 3: Add the two properties + Sanitize clamp**

- [ ] **Step 4: Run tests, verify pass** — `dotnet test --filter AppSettingsTests`.

- [ ] **Step 5: Commit** — `feat: add LockFontSize and LockedFontSize settings`.

---

### Task 4: `LyricPanel` — shared lyric rendering control

**Files:**
- Create: `src/APMLyrics/Ui/LyricPanel.xaml` + `.xaml.cs`

**Interfaces:**
- Consumes: `LineWindow.FitFontSize`/`EffectiveRows` (Task 1), `AppSettings`.
- Produces: `public partial class LyricPanel : UserControl` with
  `void Apply(AppSettings settings)` (style + effective radius), `void Show(LyricsDoc? doc, string title, string artist, bool loading)` (content; the three empty states from today's `Render` — nothing playing / loading / no lyrics), `void Tick(TimeSpan position)` (advances highlight; 180 ms crossfade on current-line change; no-ops when the line index hasn't changed), `double FittedFontSize { get; }` (exposed for tests).
  Visual tree: vertical `StackPanel` centered both axes; per-line `TextBlock`s generated in code (current: bold weight 600, full color; neighbours: 0.7× size, `NeighbourOpacity`); all `TextAlignment="Center"`, `TextWrapping="Wrap"`; uniform spacing `Margin` top/bottom = current size × 0.25.

- [ ] **Step 1: Write failing test (logic-level)**

In `tests/APMLyrics.Tests/LineWindowTests.cs` (or a new `LyricPanelTests` using a STA thread helper if one exists in `LineWindowTests`; if constructing WPF controls in tests is not already done there, test the pure sizing path via `LineWindow` and keep `LyricPanel` verified by the manual checklist instead — decide by whether `LineWindowTests` instantiates WPF types; it does not, so take the manual-checklist route): assert via `LineWindow.EffectiveRows` that a 1-row panel and a 3-row panel disagree — this pins the shedding contract `LyricPanel` relies on. (No new WPF-in-test infrastructure.)

- [ ] **Step 2: Run, verify pass** (this test guards the contract; the control itself is XAML).

- [ ] **Step 3: Implement `LyricPanel`**

Move the rendering logic out of `OverlayWindow.Render`/`ApplyFontSize`; `OverlayWindow` delegates to it. Single-line mode = panel showing only the current line (same control, radius 0). The 180 ms `DoubleAnimation` on the current line's `Opacity` moves here, fired only on index change.

- [ ] **Step 4: Manual verification checklist (in `docs/manual-testing.md` style, appended)**

Overlay shows centered text in both modes; single-line is vertically centered; spacing consistent; crossfade on line change only; resize refits live. (Executor with no interactive session: build passes + `dotnet test` green, and the checklist item is noted as pending manual sign-off.)

- [ ] **Step 5: Commit** — `feat: shared centered LyricPanel control with hybrid font sizing`.

---

### Task 5: Rewire `OverlayWindow` around `LyricPanel` + drop clamp

**Files:**
- Modify: `src/APMLyrics/Ui/OverlayWindow.xaml`, `OverlayWindow.xaml.cs`, `ResizeGrip.cs` (only if hit-test math assumed panel names)

**Interfaces:**
- Consumes: `LyricPanel` (Task 4), `DropClamp` (Task 2), `AppSettings` new fields (Task 3).
- Produces: unchanged public surface of `OverlayWindow` (`ApplySettings`, `Apply(Track?)`, `Apply(LyricsDoc?)`, `NoLyrics`, `Tick`, `SetClickThrough`, `SettingsChanged`) — `App.xaml.cs` must not change.

- [ ] **Step 1: Write failing test**

`OverlayWindow` is WPF; per Task 4's decision, no WPF-in-test infrastructure. Instead add `DropClamp` integration assertions already covered in Task 2, and a regression test in `LineWindowTests`: `Autofit_grows_with_window` (Task 1) is the pinned regression for the refit-on-resize bug. Add here: `Resize_refit_is_recomputed_on_every_size_change` is not testable headless — so the deliverable check is: `OverlayWindow.xaml.cs` calls `LyricPanel.Apply` on `OnRenderSizeChanged` (verified by code review step below).

- [ ] **Step 2: Rewire the window**

`OverlayWindow.xaml` keeps `Backdrop` + drag handler, contains `LyricPanel` only; `ApplyFontSize`/`Render` deleted, delegated to `LyricPanel`. After `DragMove()` returns in `Backdrop_MouseLeftButtonDown`, and in `LocationChanged`, apply `DropClamp.Clamp` with `SystemParameters.WorkArea.Bottom` and `SystemParameters.VirtualScreenHeight`. Persist via existing `SettingsChanged` path.

- [ ] **Step 3: Build + full test run**

Run: `dotnet build && dotnet test` — Expected: build clean, all tests PASS.

- [ ] **Step 4: Commit** — `feat: overlay uses LyricPanel; drops clamp out of taskbar`.

---

### Task 6: Logo assets

**Files:**
- Create: `assets/logo.svg` (source of truth), `src/APMLyrics/Assets/app.ico` (16/32/48/256), `src/APMLyrics/Assets/logo-256.png`
- Modify: `src/APMLyrics/APMLyrics.csproj`

**Interfaces:**
- Produces: `.ico` embedded as WPF `<Resource>` and referenced by csproj `<ApplicationIcon>`; PNG used by README (Task 8).

- [ ] **Step 1: Author SVG**

Rounded rect (rx 22% of size) fill `#fa2d48`, white "♪A" centered, glyphs sized to stay legible at 16px — if the `♪` codepoint renders thin at 16px, draw the note as circle + stem path instead (builder's call, verify by rendering the 16px PNG and eyeballing).

- [ ] **Step 2: Export ico/png** (any local tool available — e.g. ImageMagick `magick convert`; if unavailable, generate PNGs from the SVG at 16/32/48/256 and assemble with `magick` or a tiny C# script; record the exact commands used in the commit message).

- [ ] **Step 3: Wire csproj + tray**

`<ApplicationIcon>Assets\app.ico</ApplicationIcon>`, `<Resource Include="Assets\app.ico" />`; `TrayIcon.cs` replaces `SystemIcons.Application` with the packed-uri icon (H.NotifyIcon 2.1.4 `GeneratedIcons` or `BitmapImage` from pack URI — whichever compiles cleanly).

- [ ] **Step 4: Verify**

`dotnet build` clean; run app → tray icon shows ♪A (or note-as-passing if headless: build-only + note pending manual sign-off).

- [ ] **Step 5: Commit** — `feat: add ♪A app logo to exe, window, and tray`.

---

### Task 7: Settings window redesign

**Files:**
- Modify: `src/APMLyrics/Ui/SettingsWindow.xaml` + `.xaml.cs` (full rewrite)
- Consumes: `LyricPanel` (preview), `AppSettings` new fields.

**Interfaces:**
- Produces: same outward contract as today — `SettingsWindow` raises `SettingsChanged(AppSettings)` live (no Apply button), constructed as today from `TrayIcon`.

- [ ] **Step 1: Layout skeleton**

Window: `Width="520" MinWidth="480"`, app icon, title "APM Lyrics settings". Left `ListBox` nav (Appearance / Colors / Behaviour / About) ~140px; right content in a `Grid` with four panels toggled by selection.

- [ ] **Step 2: Pages**

- Appearance: mode radio buttons, neighbour radius slider (0–4), font family combo, "Lock font size" checkbox + font size slider (10–96) whose label reads "Font size (locked)" when the checkbox is on (same slider, repurposed; value seeds from current size on first lock).
- Colors: the four colour boxes + backdrop/neighbour opacity sliders + dim checkbox, plus live `LyricPanel` preview at the bottom rendering a fixed sample doc ("And I know it's not much / But it's the best I'll ever do / And I know it's not much").
- Behaviour: click-through checkbox, note that the tray menu is the recovery path.
- About: logo image, app name + version (`AssemblyVersion`), link to the repo.

- [ ] **Step 3: Wire live preview + events**

Every control updates the in-memory `AppSettings` and raises `SettingsChanged` immediately (existing funnel in `App.xaml.cs` handles debounce/persist). Preview also lives in Appearance page (radius/mode changes visible).

- [ ] **Step 4: Verify** — `dotnet build && dotnet test` green; manual checklist note for visual sign-off.

- [ ] **Step 5: Commit** — `feat: sidebar-categorized settings window with live lyric preview`.

---

### Task 8: Installer + README branding

**Files:**
- Modify: `install/APMLyrics.iss`, `README.md`

- [ ] **Step 1:** Point Inno Setup `SetupIconFile`/wizard images at the new `.ico`/PNG.
- [ ] **Step 2:** Add `docs/images/logo-256.png` (or repo-root `assets/`) reference to README next to the title.
- [ ] **Step 3:** Build installer locally if ISCC is present (`iscc install/APMLyrics.iss`); else note in commit message that CI produces it.
- [ ] **Step 4: Commit** — `feat: brand installer and README with the new logo`.

---

### Task 9: Final verification

- [ ] `dotnet build -c Release && dotnet test` — green.
- [ ] Append the manual QA checklist (overlay centered both modes, resize small→floor/shed, resize large→grows, lock on→fixed size, drag-to-taskbar→clamped, tray icon, settings pages + preview, old settings file migration) to `docs/manual-testing.md`.
- [ ] Commit — `chore: manual QA checklist for the UI overhaul`.
