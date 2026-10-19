# APM Lyrics — Update Check Design

Date: 2026-10-19
Status: approved in brainstorming (notification + manual "Update now")

## Intent and why

Users install from GitHub Releases and have no way to learn a new version exists
(the author's own machine still runs an old build). The app needs an in-app
update path: a check on startup, a visible notification, and a one-click update.

## Decisions

1. **Source of truth: GitHub Releases.** The app is built and released there
   (`.github/workflows/build.yml`, Inno Setup artifact `APMLyricsSetup.exe`).
   Check: `GET https://api.github.com/repos/<owner>/<repo>/releases/latest`
   (anonymous, no auth; 60 req/h per IP is plenty for a per-start check).
   Version compare: parse `AssemblyVersion` (major.minor.build) vs the release's
   `tag_name` (`v` prefix stripped); newer tag wins on simple version ordering,
   not string compare.
2. **Never install silently.** On startup (and on manual check from the tray
   menu and the About page), the app only *notifies*: a tray balloon + a "Check
   for updates" entry that reports status. When a new version exists, the UI
   shows "Update now" — the user clicks it when they choose.
3. **"Update now" flow:** download the release asset named `APMLyricsSetup.exe`
   to `%TEMP%\APMLyricsSetup-<version>.exe`, verify the download completed,
   then launch it with the Inno Setup `/SILENT` flag (shows a progress page but
   no questions), and exit the app so files are not locked. If the download
   fails (offline, asset renamed), fall back to opening the release page in the
   default browser.
4. **Auto-check cadence:** once per app start, at most. No timers while
   running. The check is fire-and-forget; failure is silent (offline is normal).
5. **Config:** none. No settings for update behaviour in v1 (YAGNI); a failed
   check must never bother the user.

## Non-goals

- Delta updates, checksum verification, or a staging channel.
- Any UI beyond: tray menu item "Check for updates…", the balloon/About status
  text, and the "Update now" action.
- Auto-download in the background.

## Constraints

- No new NuGet packages: use `HttpClient` (already used) and
  `System.Diagnostics.Process` to launch the installer.
- All release-check logic must be pure/testable behind a seam
  (`IReleaseFeed`-style interface) so tests cover version comparison, asset
  selection, and "no update" paths without network.
- UI copy in English. The repo slug is read from a single constant, set to the
  actual GitHub repository.
- App version must be stamped from the build so comparisons are meaningful:
  wire `AssemblyVersion`/`FileVersion` to a version that CI bumps per release
  (check build.yml; if CI does not stamp versions, at minimum keep the csproj
  version and the release tag in sync manually and document it).

## What is left to the builder

- Exact tray/About presentation of "up to date" vs "version X available".
- Where the update check runs (async after startup, marshalled to UI thread).
- How the app exits before launching the installer (order: start process, then
  Shutdown, with a small delay if needed for file locks).

## Builder-check resolutions (my calls)

- Repo slug constant: `mochrido/apm-lyrics` (from `git remote get-url origin`).
- Version stamping: releases are manual today; the release process is — bump
  `<Version>` in the csproj, tag `v<version>`, upload `APMLyricsSetup.exe` as a
  release asset with that exact name. Documented in README (release section).
- Tag parsing: strip optional `v` prefix, split on `.`; unparseable or
  non-numeric components = "no update" (silent). 4-part tags compare
  part-by-part, missing parts = 0.
- Installer flags: `/SILENT /NORESTART /SUPPRESSMSGBOXES`. The existing
  `[Run]` postinstall entry has `skipifsilent`, so the installer will NOT
  relaunch the app — no single-instance mutex conflict (#10).
- Exit ordering: download to `%TEMP%\APMLyricsSetup-<version>.exe` (overwrite
  allowed), `Process.Start` the installer, then `Application.Current.Shutdown()`
  immediately — OnExit disposal happens before the installer's first file copy
  in practice; if a lock ever shows, the installer's retry prompt is visible in
  /SILENT mode, so the user still recovers.
- Download verification: `IsSuccessStatusCode` + full stream read to file; any
  exception = browser fallback to the release page.
- Notification: H.NotifyIcon `ShowBalloonTip`; on Win11 it surfaces as a toast.
  If suppressed, the tray menu item and About page still carry the status —
  they are the authoritative UI, the balloon is best-effort.
- Re-entrancy: a single `UpdateService` instance owns the state machine
  (Idle → Checking → Available → Downloading); "Update now" is disabled unless
  state == Available.
- Acceptance: tests cover — newer tag → update available; equal/older/unparseable
  tag → up to date; asset named APMLyricsSetup.exe selected from asset list;
  download failure → fallback path chosen; offline check → silent no-op.
