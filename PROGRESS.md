# Focus Key — Project Progress & State Checkpoint

## Current Goal
Repository takeover bug-fix & Reports reliability pass: Quick Overlay keyboard focus, Reports always entering on Week against the live date, a root-cause fix for stale Reports dates, and a safe one-time correction for future-dated historical/sample focus data.

_Prior goal (completed, retained for context):_ Import historical monthly focus data from screenshots into Focus Key's SQLite database without fabricating session records.

## This Pass — Bug Fixes & Reports Reliability (2026-09-25)

### Investigation & baseline
- Branch `native/phased-rewrite`. Baseline before changes: `dotnet test` 1,141 passed / 0 failed / 0 skipped; `dotnet build` (App) 0 warnings / 0 errors.
- Reconciled documentation → code → tests → runtime for the Reports date lifecycle, the Quick Overlay focus model, and the historical import.

### Issue 1 — Quick Overlay focus/keyboard (Part 2)
- Root cause: a visible X/Close button (`CloseButton`, glyph `E8BB`) lived in the overlay header — the first focusable control in the visual tree — so activation focus could land on it, and it added a control outside the intended fast keyboard flow.
- Fix: removed `CloseButton` from `QuickOverlayWindow.xaml` and its `OnCloseButtonClick` handler. `Esc` still dismisses (`OnPreviewKeyDown`). Idle initial focus goes to **Work** via the existing `FocusSelection()`; active-session focus goes to Pause/Continue, with Left/Right/Tab/Shift+Tab/Enter/Esc navigation preserved. The presenter is borderless, so no focusable window chrome remains.

### Issue 2 — Reports must always enter on Week (Part 3)
- Root cause: `ReportsController` persists for the app lifetime; `OpenAsync()` did not reset `Period`, so a prior Month/Year selection survived a Hide/Open navigation.
- Fix: `OpenAsync()` now resets `Period = Weekly`, re-attaches to the clock (`_followCurrent = true`), and sets `Date = currentDate()` on every entry into Reports.

### Issue 3 — Stale Reports date (Part 4)
- Root cause (shared, not Week-only): the same missing entry reset. After any `SelectAsync` (which sets `_followCurrent = false`), a later Hide/Open kept the pinned date because `OpenAsync` never re-attached to the clock. The time source itself was already correct — `ReportsService` reads `TimeProvider`/`TimeZoneInfo` live on every read, and `ReadAsync` derives all ranges from the date passed at call time. The staleness was purely the controller's entry lifecycle, so the single `OpenAsync` fix corrects Week, Month, and Year at the shared layer. Internal refreshes (settings reload, manual Refresh, OS clock-change/resume) still preserve an actively-chosen Month/Year.
- Across midnight: no polling added. On the next navigation into Reports, or the next refresh while following current (including the clock-change/resume event that already calls `RefreshPages`), the date recomputes.

### Issue 4 — Future-dated historical/sample data (Part 6)
- Origin: a previous session's bulk import spread each month's total across every calendar day (`base = total/days`, `remainder = total%days`). Run mid-month, it dated the tail days after "today". That generator was an external, ephemeral tool — it is **not committed anywhere in this repository**, and the only in-repo import path (the CSV importer) stores explicit dated rows and cannot reproduce the month-distribution bug.
- Data-state finding (read-only inspection, no DB mutated): **no current database contains the affected import.** Inspected `%LOCALAPPDATA%\FocusKey\focus_key.db` (orphaned pre-rewrite schema: `focus_sessions`/`schema_version`), `focus_key.before-history-import-*.db` (0 historical rows), `focus_key.db.bak` (8 historical rows Sep 4–11, none future), `.smoke/.../focus_key.db` (0 historical rows), and the `FocusKey_backup` copies — none hold future-dated `historical_focus` rows. There was nothing to migrate in place; no live or backup database was modified.
- Correction capability (repository-local maintenance tool, committed + tested): lives in `tools/FocusKey.HistoricalRepair/` — **not** in the shipped app/Foundation and never invoked at runtime. `HistoricalFocusRedistribution` (pure, deterministic, idempotent) reclaims future-dated seconds and folds them back into the **same month's** days strictly before today (`monthStart <= target < today`), preserving each corrected month's total exactly (and therefore yearly/overall totals); native sessions are never touched. If a month has no in-month day before today (today is the 1st, or the month is fully future), that month is **skipped and reported for manual review with zero mutation** — never dumped onto today, never moved across months. The tool defaults to a read-only **dry run**; mutation requires `--apply` plus an explicit `--database`/`--data-root` target, runs in one all-or-nothing transaction, and honours cancellation.

### Tests (deterministic; fake clock / temp SQLite)
- `ReportsControllerTests`: entry-always-Week regardless of prior period; reopen resolves the current date against an advanced clock (incl. month boundary); active refresh preserves a pinned Month; clock-tracking plus pinned-during-active-use.
- `ReportsDateLifecycleTests`: current week recalculates across midnight; current month/year resolve across a year boundary.
- `HistoricalFocusRedistributionTests` (in the test project, exercising `tools/FocusKey.HistoricalRepair`): empty plan when no future rows; mid-month fold into same month before today preserving the total; determinism; **day-1 month skipped for manual review with zero mutation**; **fully-future month skipped with no cross-month redistribution**; sparse targets exercising the INSERT branch; multi-project isolation; transaction rollback on failure; cancellation leaves no partial writes; dry run writes nothing; `source_hours` preserved on existing rows and synthesized only for new rows; native sessions untouched.

### Verification
- `dotnet test tests/FocusKey.Foundation.Tests/FocusKey.Foundation.Tests.csproj -c Release`: **1,159 passed / 0 failed / 0 skipped** (also builds `tools/FocusKey.HistoricalRepair`).
- `dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release`: **0 warnings / 0 errors**.
- Maintenance tool exercised end-to-end against a controlled temporary copy: dry run reported 5 future rows and wrote nothing; `--apply` deleted 5 / upserted 24; re-apply was idempotent (0 future); `--today 2026-09-01` reported the month as manual-review with 0 proposed target days and no changes.
- Quick Overlay focus behaviour needs human visual confirmation (no UI automation in this repo). Manual steps: open the overlay idle → focus is on Work (no X control exists); arrows switch Work/Break, Enter starts, Esc closes; with a running/paused session, Tab/Shift+Tab/arrows move among Pause/Continue/Start New with no focus trap and no chrome focus target; reopen repeatedly and switch idle↔active.

### Scope
- No new productivity features, dependencies, schema changes, or unrelated refactors. Reports read logic and totals were not patched; the fixes are at the lifecycle and data layers only. The pre-existing untracked `old data/` screenshots and other unrelated working-tree state were left untouched.

### Corrective pass after adversarial review (2026-09-25)
An independent adversarial review ran; its findings were addressed as follows:
- **Overlay mouse-close tradeoff — accepted, no code change.** Removing the X also removed the only in-overlay mouse close control in the active-session state (Esc and click-away still dismiss). This is intentionally accepted for the keyboard-first design; no X and no other focusable close control was re-added.
- **Redistribution policy corrected.** The previous edge behavior (fold onto today when today is the 1st; cross-month fallback for fully-future months) was rejected and replaced by the authoritative policy above: only within the row's own month, only `monthStart <= target < today`, otherwise **skip + manual review** with zero mutation. The test that asserted "day-1 folds onto today" was removed and replaced with a skip test.
- **Dead production surface removed.** `SqliteHistoricalFocusRepository.RedistributeFutureDatedAsync` and the `HistoricalFocusRedistribution` types were removed from Foundation and relocated to the new maintenance tool `tools/FocusKey.HistoricalRepair/`. The Focus Key runtime contains **no** historical-repair behavior and no unused repair API. The tool is added to `FocusKey.slnx`; the test project references it for coverage.
- **`source_hours` provenance preserved.** Repaired existing rows keep their original imported `source_hours`; only newly created target rows carry a synthesized value. Covered by a dedicated test.
- **Reports re-click (low priority, unchanged).** Re-clicking the Reports nav item while already on Reports calls `OpenAsync` and resets to Week. Left as-is per instruction; documented known low-priority behavior.
- **Week-selector pinning (pre-existing, unchanged).** Clicking the active Week segment calls `SelectAsync` and disables follow-current until "Current"/re-entry. Pre-existing; does not break the verified lifecycle; left unchanged.
- **Temporary review artifact removed.** `D:\fkreview` (the reviewer's isolated read-only diagnostic) was found and deleted; no repository files were affected.

### Reports lifecycle deep pass (2026-09-25)
Deep root-cause pass on the Reports date/navigation lifecycle (Week view especially).
- **Root cause (what owned the stale state):** `ReportsController` is a single instance that lives for the whole app (created once in `ReportsView`, which is created once in `MainWindow`). It owns `Period`, `Date`, and `_followCurrent`. The prior fix reset those correctly on *page entry* (`OpenAsync`), but the *in-page* segmented selector called `SelectAsync(period, _reports.Date)`, which (a) turned **follow-current off** and (b) reused the controller's existing `Date`. So after clicking any segment, the view was pinned to whatever `Date` was captured earlier; `RefreshAsync` only re-resolves the date when `_followCurrent` is true, so a refresh (or crossing midnight while the app stayed open) kept the old anchor. The `CalendarDatePicker` could compound this: a programmatic `Date` echo during `Render()` could fire `DateChanged` after the `_rendering` guard cleared and silently pin the date.
- **Before:** enter Reports → Week/current (correct, prior fix). But: click Month then Week → Week anchored to the stale load-time date; Refresh on "current" Week after midnight → still yesterday; picker echo could strand follow-current.
- **After:** a new explicit ownership split — the segmented selector calls `ReportsController.SelectPeriodAsync(period)`, which means "show the CURRENT period of this type": it re-anchors to the current local date and keeps following the clock. `SelectAsync(period, date)` (date picker) still pins a specific historical date; `MoveAsync` (prev/next) still browses history pinned; `CurrentAsync` still returns to current. Result: clicking Week always lands on the real current week; Refresh on the current week re-resolves today; crossing midnight is reflected on the next refresh/selection/entry without restart; deliberately-browsed historical weeks/months stay put across refresh (not snapped to today). The picker `DateChanged` now ignores programmatic echoes (only a genuinely different pick pins a date).
- **Semantics:** `Today` = live local date from the existing `TimeProvider`/`TimeZoneInfo` clock (`ReportsService.CurrentDate`); `DisplayedPeriod` = `Period`; following-current vs pinned governs whether the anchor tracks `Today`. No `DateTime.Now`/`Today` sprinkled in; the existing clock abstraction is used throughout; week/month/year boundaries, aggregation, and chart semantics were not changed.
- **Files:** `src/FocusKey.Foundation/Reports/ReportsController.cs` (+`SelectPeriodAsync`), `src/FocusKey.App/ReportsView.cs` (segment → `SelectPeriodAsync`; hardened picker `DateChanged`).
- **Tests added** (`ReportsControllerTests`, deterministic fake clock): `SelectPeriodTracksCurrentDateAndFollowsClock`, `SelectingWeekReturnsToCurrentWeekAfterHistoricalNavigation`, `HistoricalSelectionStaysHistoricalAcrossRefresh` — alongside the existing entry-Week, reopen-resolves-current-date, midnight, and active-refresh tests. Full suite: **1,162 passed / 0 failed / 0 skipped**.
- **Manual (WinUI) still required:** sequences A–E in the task (segment/nav round-trips, midnight rollover, no stale headings/chart/labels) need human confirmation; unit tests cover the controller lifecycle but not the rendered WinUI surface.

### V1.0.0 release-candidate preparation (2026-09-25)
Audit → package → verify → present. No release was published; nothing committed, pushed, or tagged.
- **Version source of truth:** `src/FocusKey.App/FocusKey.App.csproj` (`Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion` = 1.0.0 / 1.0.0.0). `installer.iss` mirrors `AppVersion=1.0.0` / `VersionInfoVersion=1.0.0.0`. Two declarations by design (app metadata + Inno Setup); documented here.
- **Packaging architecture (existing, reused):** self-contained `win-x64` `dotnet publish` → Inno Setup (`installer.iss`) → per-user `FocusKeySetup.exe` (installs to `%LOCALAPPDATA%\Programs\Focus Key`, `PrivilegesRequired=lowest`, `CloseApplications=force`). The installer packages `publish\*` only.
- **Clean build + tests:** cleared stale `publish/`, `dotnet clean` (App, Release), fresh `dotnet publish -c Release -r win-x64 --self-contained true -o publish` → 0 warnings / 0 errors. `dotnet test …Foundation.Tests -c Release` → **1,162 passed / 0 failed / 0 skipped** (unchanged from baseline).
- **Publish artifact verified:** `publish/` = 231 MB, 526 files; contains `FocusKey.exe` (361,472 bytes), `e_sqlite3.dll`, `Assets\AppIcon.ico`, all four sound WAVs, WinAppSDK + .NET runtime. **Excludes** tests, source, the `FocusKey.HistoricalRepair` tool, `old data/`, any `*.db`/`*.log`/`*.pdb`. SHA-256 of `publish/FocusKey.exe` = `4b8940086c592dda4e0532ea21534a73b5a18aea2f28a974c363bfd433ddc97d` (informational).
- **Security/local-first audit:** no secrets/tokens/dev paths/preview flags in shipped source; the only network-related reference is a comment asserting "no telemetry, no network, no analytics." No `HttpClient`/socket/telemetry in `src/`. `FOCUSKEY_DATA_ROOT` remains opt-in (unset by default). Confirmed local-first.
- **User data:** DB + settings + logs at `%LOCALAPPDATA%\FocusKey\`, separate from the install dir; reinstall/uninstall leaves user data in place. No developer database is bundled (publish excludes `*.db`).
- **Presentation:** added `RELEASE_v1.0.0.md` (paste-ready GitHub Release body, user-facing, verified feature claims only); README gained the tagline and an explicit "does not monitor …" privacy line. `RELEASE_NOTES.md` left as-is.
- **Performance (observation):** idle working set of the running Release build was ~118 MB; no active-session CPU load observed at idle. Precise CPU sampling and install-size-on-disk are manual follow-ups.
- **Installer build & lifecycle (completed 2026-09-25, Inno Setup 6 now installed):** compiled `installer.iss` with Inno Setup 6 (`ISCC.exe`) against the fresh `publish/`. Produced **`artifacts/release/1.0.0/FocusKeySetup.exe`** — 64,716,187 bytes, built 2026-09-25 16:12, SHA-256 `307d1b08caef58b1462ce8b5bcc9cd5dd1bffcd790f3df251b8a57c201843eca` (distinct from the stale 64,692,688-byte Sep-22 build). `RELEASE_v1.0.0.md` now carries this real checksum.
  - **Update-install** (RC over the old Sep-22 install): success, single uninstall entry, installed `FocusKey.exe` refreshed to the RC build.
  - **Reinstall** (same version again): success, still a single uninstall entry.
  - **Installed-build smoke on a clean data root** (`FOCUSKEY_DATA_ROOT` → temp, first-run simulation): clean launch, DB created + migrated to v14, `NoActiveSession` recovery, main window + tray, `Shift + F3` registered, single-instance ownership. Second launch transferred focus to the first and exited (log: "Shell activation: ShowWindow") — single-instance confirmed. Work/Break/Stop/Reports/Settings interaction is covered by the automated suite + the earlier manual A–E; per-control clicking of the installed GUI remains a human step.
  - **Uninstall** (silent): program dir removed, uninstall registry entry gone, and `%LOCALAPPDATA%\FocusKey\focus_key.db` **preserved** (81,920 bytes, untouched) — uninstall does not delete user data.
  - **Clean install** (no prior install): success; the RC is left installed on this machine.
  - **Finding — local dev DB malformed:** `%LOCALAPPDATA%\FocusKey\focus_key.db` became `SQLite Error 11: database disk image is malformed` (surfaced when launching against the real data root during concurrent-launch/force-close lifecycle testing). The app currently hard-fails startup on a corrupt DB rather than recovering. This is the developer's local file (originally an orphaned pre-rewrite DB, migrated this session; no genuine current-schema user history), left untouched per the "do not modify user data" rule — recommend the user delete/rename it to get a fresh DB. Graceful corrupt-DB recovery is a **post-v1** robustness item, not implemented here (no feature work in this pass).
  - **Screenshots:** not captured — no reliable, private-data-safe WinUI capture in this environment; `RELEASE_v1.0.0.md` keeps a screenshots placeholder for a human to fill.






## Current Checkpoint
Implemented and verified:
- **1. Historical Focus Data Import**:
  - Calibrated and parsed all 12 monthly focus totals from user screenshots across August 2025 – September 2026.
  - Calculated exact daily distribution across all calendar days of each month (`base = total / days`, `remainder = total % days`) without rounding drift.
  - Backed up SQLite database to `%LOCALAPPDATA%\FocusKey\focus_key.before-history-import-20260922-170534.db`.
  - Inserted 364 rows into `historical_focus` table inside an immediate transaction.
  - Preserved 21 existing native work sessions with zero fake session rows created.
- **2. Runtime UI & Reports Verification**:
  - Verified Reports Month view (September 2026: 114h 23m total focus time, 16 completed sessions, 76.2% completion rate, 4 active weeks).
  - Verified Reports Year view (2026: 658h 14m total focus time, 8 of 9 active months).
  - Verified Reports Year view (2025: 104h 56m total focus time across Aug–Nov 2025, 0 work sessions, 4 active months).
  - Verified streak calculation (Current streak 53 days, Longest streak 181 days).
  - Verified year eligibility enabled by historical date range spanning > 365 days.
- **3. Final Release Self-Contained Distribution**:
  - `dotnet publish src/FocusKey.App/FocusKey.App.csproj -c Release -r win-x64 --self-contained true -o publish` completed with 0 errors.
  - Final application version metadata: `Version=1.0.0`, `AssemblyVersion=1.0.0.0`, `FileVersion=1.0.0.0`, `InformationalVersion=1.0.0`.
  - All high-fidelity audio assets (`start_tick.wav`, `session_action.wav`, `complete.wav`, `completion_bell.wav`), application icon (`Assets\AppIcon.ico`), WinUI 3 binaries, SQLite interop (`e_sqlite3.dll`), and runtime dependencies verified in output. Zero debug/pdb/scratch files in publish output.
- **2. Inno Setup Standalone Installer Packaging**:
  - Built `artifacts\release\1.0.0\FocusKeySetup.exe` (64,692,688 bytes, ~61.70 MB, SHA-256: `7F3F43F160C0AC352CA0C09C3BDA95F1240A6311BDD0276F84A6395629B72089`) via Inno Setup 6 with LZMA2/ultra64 solid compression.
  - Per-user installation to `%LOCALAPPDATA%\Programs\Focus Key` without requiring UAC administrator elevation.
  - Configured `CloseApplications=force` for seamless silent upgrades of running instances.
  - Start Menu and optional Desktop shortcuts configured.
  - Quiet startup and background shell registry entry configured.
- **3. Full Runtime Lifecycle QA & Upgrade Verification**:
  - Clean upgrade installation from RC1 to final v1.0.0 verified.
  - Single uninstall entry in Windows registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-FOCUS-KEY1-0000-000000000001}_is1`).
  - Single `unins000.exe` uninstaller binary verified.
  - Main window HWND creation and interactive launch verified.
  - Single-instance mutex lease verified (secondary launch transfers focus and exits cleanly).
  - Start with Windows registry key verified without development path references.
  - Silent uninstallation verified removing binary directory while preserving `%LOCALAPPDATA%\FocusKey\focus_key.db`.
  - Clean reinstall verified.
- **4. Automated Tests**: 1,141/1,141 unit tests passing (`dotnet test -c Release`), 0 failed, 0 skipped.
- **5. Build**: 0 Warning(s), 0 Error(s) (`dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release`).

## Completed Work
- **Visual & UX Audit**: Comprehensive desktop/full-screen audit documented in `VISUAL_AUDIT.md`.
- **Visual System Definition**: Carbon Studio / Fluent design system specified in `VISUAL_SYSTEM.md`.
- **Stage 1 (Foundation)**: Design tokens, contrast corrections, typography styles, and 3-tier geometry (`b52919f`).
- **Stage 2 (Today Page Initial Structure)**: Responsive centering, idle hero launcher, active session zero-layout-jump presentation (`7c94c90`).
- **Stage 3 (Navigation Shell, Session Behavior & Window Management)**: Destructive Stop interaction replaced with non-destructive Pause / Continue / Start New lifecycle; minimize to tray with single-instance activation; dual global shortcuts (`Shift + F3`, `Shift + F4`) (`4e5e783`).
- **Stage 4 (Reports Redesign: Cohesive Desktop Dashboard)**: Dominant chart hero (~72%), 3 compact summary metrics, secondary contextual insights rail (~28%), reflow to stacked, truthful period comparison & insights.
- **Stage 5 (Yearly Reports)**: Data-driven eligibility, 12-month calendar aggregation, dynamic yearly ceiling/intervals, truthful prior-year comparison and insights, and future navigation clamping.
- **Stage 5b (Reports Acceptance Refinement & Temporary Preview)**: Balanced streaks group, zero metric repetition, comprehensive consistency metrics across all periods, and temporary `FOCUSKEY_YEARLY_PREVIEW` override.
- **Stage 6 (Today Final Hero Redesign)**: Maximized 3-tier desktop composition, unified zero-layout-jump hero surface, semantic Pause/Continue/Start New actions.
- **Stage 6b (Reports Insights Vertical Composition + Today Summary Polish + Yearly Preview Verification)**: Proportional 4-zone grid distribution in Reports Insights rail, polished Today Summary 2x2 grid with horizontal divider.
- **Stage 7 (Settings + Quick Overlay Final Refinement)**: Draggable Quick Overlay with multi-monitor clamping and SQLite position persistence, unified shell across Idle/Running/Paused states, Settings live theme refresh, dual shortcuts with collision prevention, 12/24-hour time format preference in Today activity.
- **Stage 8 (Today + Quick Overlay Final Acceptance Polish)**: Removed visible Stop button from Running (Pause only) and Paused (Continue + Start New side-by-side) states across Today and Quick Overlay; ~310 DIP top row Today geometry with centered Idle cards and 2x2 metric grid.
- **Stage 9 (Window Close Experience: Hide or Quit)**: Modal `ContentDialog` on main window close with clear `Hide Focus Key` (primary) vs. `Quit Focus Key` (secondary) choice; single-dialog reentrancy protection; canonical shutdown reuse; direct minimize preservation; running/paused session preservation (`0a1f457`).
- **Stage 9b (Quick Overlay Surgical Visual Restore)**: Surgically restored Quick Overlay visual presentation and geometry to approved `a19b522` baseline (480 DIP width, 432 DIP progress bar, left-aligned 52 DIP timer, persistent header dot + title, right-aligned buttons).
- **Stage 10 (Today Native Windows Adaptive Redesign)**: Adaptive navigation shell (Expanded 220 DIP, Compact rail 54 DIP, Collapsed 0 DIP with drawer), 2-column desktop composition on Wide/Medium and single vertical stack on Narrow, zero-layout-jump Session Hero with integrated idle switcher, restrained active-session navigation status dot, and unclipped timer digit typography.
- **Stage 10b (Post-Today UX Refinement Pass)**: Keyboard navigation & activation (Left/Right arrows for idle selector, Up/Down for nav, Esc for drawer), preserved session-colored Today status dot, stable Today activity overflow scrolling (420 DIP max height internal viewer), 4 fixed weekly buckets in monthly reports, and enlarged draggable Quick Overlay header.
- **Stage 14 (Settings Information Architecture & Nested Card Hierarchy)**: Restructured 4 top-level sections as native Fluent cards (`SESSION`, `APPEARANCE`, `SHORTCUTS`, `ADVANCED`), sub-card grouping (`FkCardSubtle`), 48px button headers with chevron toggle and full keyboard accessibility, SQLite expansion persistence, and bottom-anchored reload footer.
- **Settings Final Polish & Session Sounds Refinement**: Prominent Settings page header, 16 DIP top vertical spacing, sub-card title dividers, darkened `#363636` borders, normalized right-edge alignment, full copy audit, master + granular session sounds with unconditional previews, and SQLite Migration 13.
- **Stage 15 (Application UI Scaling / Zoom System)**: Native, persistent, layout-aware UI scaling system (80% to 150%), global zoom keyboard shortcuts, Settings ComboBox, HUD overlay, effective width adaptive layout, single-queue authoritative synchronization, and SQLite Migration 14.
- **Repository Hygiene Cleanup**: Cleaned accidental/scratch agent files (`ORIGINAL_REQUEST.md`, `PROJECT.md`), removed obsolete Phase 11 sound mock tests (`SessionSoundCoordinationTests.cs`), and standardized test file names into clean domain stress suites (`UiScaleSettingsSyncEmpiricalTests.cs`, `UiScaleShortcutsSteppingStressTests.cs`, `UiScaleRapidAlternatingSyncStressTests.cs`, `UiScaleSettingsSyncTests.cs`).
- **Phase 17 (Full Accessibility + Windows Narrator Audit)**: Full keyboard navigation and Windows Narrator accessibility across Today, Reports, Settings, Quick Overlay, Navigation Shell, and Close/Hide Dialog with dynamic live session announcements, radio item types, collapsible section status, and composite metric descriptions.
- **Phase 18 (Final User-Selected Sound Integration)**: Integration of exact user audio assets (`start_tick.wav`, `session_action.wav`, `complete.wav`, `completion_bell.wav`), single-playback natural completion bell, and action audio cues for Pause, Continue, Stop, and Start New.
- **Phase 19 (Final Pre-Release Cleanup + Consistency + Engineering QA)**: Cleaned debug/preview flags (`FOCUSKEY_YEARLY_PREVIEW`), audited Reports (Week/Month/Year), unified sidebar button accessibility properties, verified full runtime smoke workflow (`RuntimeSmokeWorkflowTests.cs`), and confirmed 0 build warnings/errors.
- **Phase 21 (Final UI Polish Fixes & Runtime Screenshot Verification)**: Close Dialog default action & Enter routing, shortcut recording Fluent accent border, color picker bottom-edge flyout placement, and reports tooltip top clearance clamping.
- **Phase 22 (Release Candidate RC1 Packaging & Lifecycle QA)**: Self-contained `win-x64` publish distribution, aligned `1.0.0-rc.1` version metadata, and Inno Setup installer package (`artifacts\release\1.0.0-rc1\FocusKeySetup.exe`, 64,713,868 bytes, SHA-256: `892A717FDED91CDD8B72D1077CE50AECA342EDC1F4DF6D4A0D597DF446345939`).
- **Phase 23 (Focus Key v1.0.0 Final Release Promotion)**: Promoted version to `1.0.0`, compiled final standalone installer (`artifacts\release\1.0.0\FocusKeySetup.exe`, 64,692,688 bytes, SHA-256: `7F3F43F160C0AC352CA0C09C3BDA95F1240A6311BDD0276F84A6395629B72089`), verified seamless RC1-to-v1.0.0 in-place upgrade, single uninstaller registration, and complete runtime smoke workflow.

## Remaining Work
- All development, UI polish, sound integration, accessibility, packaging, RC1 testing, and v1.0.0 final release verification stages are complete.

## Important Active Decisions
- **Final Release Target**: Version metadata is `Focus Key v1.0.0` (`1.0.0` / `1.0.0.0`).
- **User Audio Provenance**: Sound files are exact user-provided WAV assets (`start_tick.wav`, `session_action.wav`, `complete.wav`, `completion_bell.wav`), unmodified and ungenerated.
- **Natural Completion Semantics**: `completion_bell.wav` contains three internal chimes recorded directly in the audio asset, played once asynchronously via Win32 `PlaySound`. Looped playback logic was removed.
- **Session Action Audio**: `session_action.wav` plays once for Pause, Continue, Stop, and Start New.
- **Preview Independence**: Preview buttons always play a single cue directly, regardless of master sound gate or child toggle states.
- **Close Dialog Default**: `Hide Focus Key` is the explicit primary action, activated immediately on Enter keypress without interception.
- **Standalone Distribution**: Self-contained per-user installer without external runtime dependencies.
- **User Data Isolation**: User SQLite database remains isolated at `%LOCALAPPDATA%\FocusKey\focus_key.db`, preserved across installer upgrades and uninstalls.
- **Signing Status**: Package is unsigned (reported truthfully).

## Last Verification (v1.0.0 release checkpoint — historical)
_Superseded for this working tree by the "This Pass" verification above (1,159 tests). Retained as the v1.0.0 release record._
- **Build**: `dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release` (0 Warnings, 0 Errors).
- **Test Suite**: `dotnet test -c Release` (1,141 passed, 0 failed, 0 skipped).
- **Publish & Installer**: `dotnet publish` (0 Errors) and Inno Setup compile (`artifacts\release\1.0.0\FocusKeySetup.exe`, 64,692,688 bytes).
- **Lifecycle QA**: Silent install, HWND creation, single instance, registry startup key, RC1-to-v1.0.0 upgrade, single uninstaller path, data preservation, and reinstall verified.

## Current Git State
- Branch: `native/phased-rewrite` (`main` untouched).
- Working tree has the uncommitted bug-fix + corrective pass described above (not yet committed): modified `PROGRESS.md`, `ReportsController.cs`, `QuickOverlayWindow.xaml`(+`.cs`), `SqliteHistoricalFocusRepository.cs`, `ReportsControllerTests.cs`, `FocusKey.slnx`, `FocusKey.Foundation.Tests.csproj`; new `tools/FocusKey.HistoricalRepair/*`, `ReportsDateLifecycleTests.cs`, `HistoricalFocusRedistributionTests.cs`. Pre-existing untracked `old data/` left untouched.
