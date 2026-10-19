# Update Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In-app update check against GitHub Releases with a manual "Update now" action.

**Architecture:** Pure, testable core (`UpdateCheck` — version/tag parsing and asset selection; `GitHubReleaseFeed` — HTTP behind `IReleaseFeed` seam); a UI-adjacent `UpdateService` state machine wired into `App` startup, the tray menu, and the Settings About page.

**Tech Stack:** WPF .NET 8, HttpClient (no new packages), H.NotifyIcon 2.1.4, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-19-update-check-design.md`

## Global Constraints

- Repo slug constant: `mochrido/apm-lyrics`.
- Tag parse: strip optional `v`, split `.`, numeric parts only; unparseable = no update.
- Installer launch: `%TEMP%\APMLyricsSetup-<version>.exe`, `/SILENT /NORESTART /SUPPRESSMSGBOXES`, then `Shutdown()`.
- Download failure → open the release page in the default browser.
- Check runs once per app start; failures are silent. UI copy in English.
- Commit trailer: `Co-Authored-By: Claude Code <noreply@anthropic.com>`.
- Test command: `dotnet test tests/APMLyrics.Tests/APMLyrics.Tests.csproj`.

## Review Focus

- Unparseable tag (`v1.2-beta`): must be "no update", never a crash or false positive.
- Offline at startup: silent, app fully functional.
- Equal version: "up to date", no download prompt.
- Asset renamed/missing in the release: browser fallback, no crash.
- Rapid double-click "Update now": single download (state machine guards).

---

### Task 1: `UpdateCheck` pure core

**Files:**
- Create: `src/APMLyrics/Core/UpdateCheck.cs`
- Test: `tests/APMLyrics.Tests/UpdateCheckTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ReleaseInfo(string Tag, string? SetupAssetUrl, string ReleasePageUrl);`
  - `public static Version? ParseTag(string? tag)` — null when unparseable.
  - `public static bool IsNewer(Version current, string? tag)` — true only when parsed tag > current.
  - `public static ReleaseInfo? PickSetupAsset(ReleaseInfo candidate)` — null when no `APMLyricsSetup.exe` asset.

- [ ] **Step 1: Failing tests** — `ParseTag_strips_v_and_parses` ("v1.2.3" → 1.2.3), `ParseTag_returns_null_for_unparseable` ("v1.2-beta", "", null), `ParseTag_fills_missing_parts_with_zero` ("v1.2" → 1.2.0), `IsNewer_true_only_for_greater_tags` (current 1.0.0: "v1.0.1" true, "v1.0.0" false, "v0.9" false, "junk" false, null false), `PickSetupAsset_selects_the_setup_exe` (asset list with other names), `PickSetupAsset_null_when_missing`.
- [ ] **Step 2: Run** — expect compile FAIL.
- [ ] **Step 3: Implement** — `char.IsAsciiDigit` per component; `Version` compare handles part count.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** — `feat: pure update-check core (tag parsing, asset selection)`.

### Task 2: `GitHubReleaseFeed` HTTP seam

**Files:**
- Create: `src/APMLyrics/Core/GitHubReleaseFeed.cs`
- Test: `tests/APMLyrics.Tests/GitHubReleaseFeedTests.cs`

**Interfaces:**
- Consumes: `ReleaseInfo` from Task 1.
- Produces: `public interface IReleaseFeed { Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct); }` and `public sealed class GitHubReleaseFeed(HttpMessageHandler? handler = null) : IReleaseFeed` — GET `https://api.github.com/repos/mochrido/apm-lyrics/releases/latest`, parse `tag_name`, `html_url`, `assets[].name/browser_download_url` (asset named `APMLyricsSetup.exe`), any failure → null. Pattern after `CatalogClient`'s handler seam (disposeHandler: false, shared static client otherwise).

- [ ] **Step 1: Failing tests** — `Returns_tag_and_setup_url` (fixture JSON via stub handler), `Null_on_http_failure`, `Null_on_garbage_json`, `Null_when_no_setup_asset`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** (~60 lines, System.Text.Json DOM parse like `AppleLyricsCache.Read`).
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** — `feat: GitHub releases feed behind IReleaseFeed seam`.

### Task 3: `UpdateService` state machine + App wiring

**Files:**
- Create: `src/APMLyrics/Core/UpdateService.cs`
- Modify: `src/APMLyrics/App.xaml.cs`
- Test: `tests/APMLyrics.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Consumes: `IReleaseFeed` (Task 2), `UpdateCheck` (Task 1).
- Produces: `public sealed class UpdateService(IReleaseFeed feed, Func<Version> currentVersion, HttpClient? downloadClient = null)`:
  - `event Action<ReleaseInfo>? UpdateAvailable;` (marshalled by the App to UI thread)
  - `Task<bool> CheckAsync(CancellationToken ct)` — false on any failure/no update.
  - `Task<bool> DownloadAndRunInstallerAsync(ReleaseInfo release, CancellationToken ct)` — downloads the asset to `%TEMP%`, `Process.Start` with the silent flags, returns true when launched; false → caller opens `ReleasePageUrl` in browser.
  - State enum `Idle/Checking/Available/Downloading` exposed as `State` property; `DownloadAndRunInstallerAsync` no-ops (returns false) unless `State == Available`… (single-flight guard: `Interlocked`/lock on transition).

- [ ] **Step 1: Failing tests** (stub feed + stub download client via handler): `CheckAsync_raises_UpdateAvailable_for_newer_tag`, `CheckAsync_false_when_no_update_or_offline`, `DownloadAndRun_refused_when_not_available` (double-click guard), `DownloadAndRun_returns_false_on_download_failure`.
  Note: `Process.Start` in tests — inject a `Action<string,string>` launcher seam `internal` for tests; default = real Process.Start. Tests assert the launcher receives the temp path + silent flags.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement**; wire in `App.OnStartup` after tray creation: `_ = Task.Run(() => _updateService.CheckAsync(...))`, on `UpdateAvailable` → `Dispatcher.InvokeAsync` tray balloon + menu enable.
- [ ] **Step 4: Run** — full suite PASS.
- [ ] **Step 5: Commit** — `feat: update service with startup check and installer launch`.

### Task 4: UI — tray menu, About page, README release docs

**Files:**
- Modify: `src/APMLyrics/Ui/TrayIcon.cs`, `src/APMLyrics/Ui/SettingsWindow.xaml(.cs)`, `README.md`, `src/APMLyrics/APMLyrics.csproj` (add `<Version>0.2.0</Version>`)

- [ ] **Step 1:** Tray menu: "Check for updates…" item → runs `CheckAsync`, then updates its header ("Up to date" / "Version X available → Update now"); "Update now" click → `DownloadAndRunInstallerAsync`, on false open `ReleasePageUrl`; balloon on startup detection ("APM Lyrics X is out — Update now from the tray menu.").
- [ ] **Step 2:** About page: current version label (exists) + "Check for updates" button that shares the same service instance (passed into SettingsWindow ctor) and shows the result inline.
- [ ] **Step 3:** csproj `<Version>0.2.0</Version>`; README "Updating" section: new builds auto-notify; manual = download `APMLyricsSetup.exe` from Releases and run it (upgrades in place — the author's current situation).
- [ ] **Step 4:** Build + full suite green.
- [ ] **Step 5:** Commit — `feat: update UI in tray and About; document releases`.
