# Focus Key — Phase History

This document contains the consolidated historical implementation records for all Focus Key development phases in chronological order.

---

## Phase 0 — Native Foundation

Status: **Complete and verified**  
Date: 2026-09-02  
Branch: `native/phased-rewrite`  
Baseline commit: `17ae9a8` ("Add Focus Key repository baseline")

### Objective
Create the smallest clean native foundation Focus Key can be built on, phase by phase, and prove it actually works on this machine.

Phase 0 established:
- A valid C#/.NET solution with clear project boundaries
- A WinUI 3 application project on the Windows App SDK
- A real automated test project
- A minimal, explicit application bootstrap
- Deterministic application data paths
- Minimal local-only logging
- Foundational handling for otherwise unhandled failures
- The smallest SQLite infrastructure that proves database initialization works
- A minimal placeholder native window
- Clean build and test workflows
- A safe Git/GitHub baseline with the work pushed to the official remote

### Implemented Scope
- **Solution:** `FocusKey.slnx` with three projects:
  - `FocusKey.App` (`src/FocusKey.App`): WinUI 3 application, bootstrap, placeholder window.
  - `FocusKey.Foundation` (`src/FocusKey.Foundation`): Paths, logging, SQLite bootstrap.
  - `FocusKey.Foundation.Tests` (`tests/FocusKey.Foundation.Tests`): Automated tests for foundation.
- **Application paths:** `AppPaths` is the single source of truth for runtime data directories (`%LOCALAPPDATA%\FocusKey` with `focus_key.db` and `logs/focus_key.log`), with `FOCUSKEY_DATA_ROOT` environment override support.
- **Logging:** `FileAppLogger` appends UTC-stamped lines to one local file. Local only: no telemetry, no network, no analytics.
- **Error handling:** `FatalError` records unhandled XAML, domain, and unobserved task exceptions; startup failure writes a fallback report (`startup-failure.log`) and displays a native message box.
- **SQLite:** Connection factory with fixed pragmas (`WAL` journal mode, `synchronous=NORMAL`, `busy_timeout=5000`, `foreign_keys=ON`), plus a forward-only migration runner (`PRAGMA user_version` + `schema_migrations` audit table). Migration 1 (`schema_metadata`) creates schema metadata.
- **Placeholder window:** Displays application name, runtime facts (version, data root, database, schema version, log file).

### Architecture Decisions
- **Two source projects, not four:** `FocusKey.Foundation` targets plain `net10.0` (zero Windows UI dependencies, testable without UI host); `FocusKey.App` hosts WinUI 3.
- **No DI container:** Direct composition in `FoundationBootstrap.Run()`.
- **Unpackaged for development:** `WindowsPackageType=None`, framework-dependent against machine-wide Windows App Runtime.
- **Architecture-neutral managed platform:** App pins `RuntimeIdentifier=win-x64`; libraries and tests remain neutral.
- **Timestamps in SQLite are ISO-8601 UTC text:** Formatted/parsed via `UtcTimestamp` (`yyyy-MM-ddTHH:mm:ss.fffffffZ`, 28 characters, tick-exact).
- **Concurrency in migrations:** Each migration runs in a `BEGIN IMMEDIATE` transaction and re-reads `user_version` inside it.

### Tests & Verification
- 38 foundational unit tests passing across `AppPathsTests`, `FileAppLoggerTests`, `DatabaseBootstrapperTests`, and `UtcTimestampTests`.
- Runtime smoke test executed twice against isolated `.smoke\appdata`:
  - Run 1: First start, applied migration 1 (`schema_metadata`), created database, displayed window, clean exit code 0.
  - Run 2: Second start against existing database, idempotent start (no re-migration), clean exit code 0.

### Known Limitations & Deferred Work
- Pre-existing user data at `%LOCALAPPDATA%\FocusKey` left untouched by redirecting smoke tests.
- No log rotation; debug output untuned.
- Deferred: session domain models, repositories, session engine, timer, recovery, single-instance, tray, hotkey, overlays, Today, Reports, Settings.

---

## Phase 1 — Session Data Layer

Status: **Complete and verified**  
Date: 2026-09-02  
Branch: `native/phased-rewrite`  
Preceding commit: `a1d1e21` (Phase 0 audit correction)

### Objective
Make session persistence correct before any session behaviour exists. Phase 1 defines durable representation in SQLite and the repository contract without timer, transitions, or workflow.

### Implemented Scope
- `SessionId`: Stable, typed application identity wrapping `Guid` (36-char lowercase text).
- `SessionType` (`Work`, `Break`) and `SessionStatus` (`Running`, `Completed`, `Stopped`, `Interrupted`), with bidirectional string mapping via `SessionTypeText` and `SessionStatusText`.
- `SessionRecord`: Persisted record with UTC normalization and shape invariants. Computed properties: `PlannedEndAt = StartedAt + PlannedDuration`, `ActualDuration = EndedAt - StartedAt`.
- Exceptions: `SessionNotFoundException`, `DuplicateSessionIdException`, `ActiveSessionAlreadyExistsException`.
- `ISessionRepository` & `SqliteSessionRepository`: `AddAsync`, `GetAsync`, `UpdateAsync`, `GetRunningAsync`, `GetStartedBetweenAsync`.
- Schema Migration 2 (`sessions`):
  ```sql
  CREATE TABLE sessions (
      id                       TEXT    NOT NULL PRIMARY KEY,
      type                     TEXT    NOT NULL,
      status                   TEXT    NOT NULL,
      started_at_utc           TEXT    NOT NULL,
      planned_duration_seconds INTEGER NOT NULL,
      ended_at_utc             TEXT,
      created_at_utc           TEXT    NOT NULL,
      CHECK (length(id) = 36),
      CHECK (type IN ('work', 'break')),
      CHECK (status IN ('running', 'completed', 'stopped', 'interrupted')),
      CHECK (planned_duration_seconds > 0),
      CHECK (length(started_at_utc) = 28),
      CHECK (length(created_at_utc) = 28),
      CHECK (ended_at_utc IS NULL OR length(ended_at_utc) = 28),
      CHECK ((status = 'running') = (ended_at_utc IS NULL)),
      CHECK (ended_at_utc IS NULL OR ended_at_utc >= started_at_utc)
  ) STRICT;

  CREATE UNIQUE INDEX ux_sessions_single_running
      ON sessions (status) WHERE status = 'running';

  CREATE INDEX ix_sessions_started_at_utc
      ON sessions (started_at_utc);
  ```

### Key Invariants & Query Semantics
- At most one session can be `Running` in SQLite, guaranteed by the partial unique index `ux_sessions_single_running`.
- Range query `GetStartedBetweenAsync` uses half-open intervals `fromInclusive <= StartedAt < toExclusive`, sorted by `started_at_utc ASC, id ASC`.
- Planned duration stored as whole seconds in `INTEGER` column; actual duration calculated dynamically.

### Tests & Verification
- 154 total tests passing (38 Phase 0 + 116 Phase 1).
- Coverage: `SessionIdTests`, `SessionCodeTests`, `SessionRecordTests`, `SqliteSessionRepositoryTests`, `SessionSchemaTests`.
- In-place schema migration from version 1 to 2 verified both in unit tests and via runtime smoke against a Phase 0 database.

---

## Phase 2 — Session Engine

Status: **Complete, verified, and pushed**  
Date: 2026-09-05  
Branch: `native/phased-rewrite`  
Implementation commit: `c68a3ff`

### Objective
Complete the UI-independent session engine with atomic lifecycle transitions. Focus Key can start Work or Break, observe active state, stop, and explicitly complete due sessions.

### API & Engine Architecture
`SessionEngine(ISessionRepository, TimeProvider? = null, SessionDurations? = null)`:
- `StartAsync(SessionType, CancellationToken)`: Validates duration, checks active state, inserts Running record immediately.
- `GetActiveAsync(CancellationToken)`: Returns point-in-time `SessionSnapshot` or null. Never mutates persistence.
- `StopAsync(CancellationToken)`: Returns `NoActiveSession`, `Stopped`, `Completed`, or `Conflict`.
- `CompleteIfDueAsync(CancellationToken)`: Returns `NoActiveSession`, `StillRunning`, `Completed`, or `Conflict`.

### Concurrency & Atomic Conditional Update
- Added `ISessionRepository.TryUpdateAsync(SessionRecord expected, SessionRecord replacement)`:
  - Compares all stored facts (`id`, `type`, `status`, `started_at_utc`, `planned_duration_seconds`, `ended_at_utc`, `created_at_utc`) in the `WHERE` clause inside an immediate transaction.
  - Exactly matching storage returns true; changed or missing row returns false.
  - Eliminates write-after-read races between multiple engine/coordinator instances without needing extra revision columns.

### Time & Lifecycle Semantics
- `PlannedEndAt = StartedAt + PlannedDuration`
- `Remaining = max(PlannedEndAt - ObservedAt, 0)`
- `Elapsed = clamp(ObservedAt - StartedAt, 0, PlannedDuration)`
- Manual user stop before deadline writes `Stopped` with observed time (clamped to `StartedAt`).
- Natural completion at or after deadline writes `Completed` with `EndedAt = PlannedEndAt`.

### Tests & Verification
- 209 total tests passing (154 baseline + 55 Phase 2).
- Cross-instance integration tests with `TaskCompletionSource` barriers proving competing starts, stop vs completion races, stale updates, and transaction rollbacks.
- Native smoke test passed two clean launches on `.smoke\p2-...`.

---

## Phase 3 — Recovery & Application Coordination

Status: **Complete, verified, and pushed**  
Date: 2026-09-05  
Branch: `native/phased-rewrite`  
Implementation commit: `a7272c1`

### Objective
Implement crash recovery rules and application-lifetime session coordination.

### Recovery Rules
- **Startup Recovery (`SessionRecovery`):**
  - Persistence initializes before recovery reads the existing Running row.
  - At or after `PlannedEndAt`: Recovery writes `Completed` with `EndedAt = PlannedEndAt`.
  - Before `PlannedEndAt`: Recovery writes `Interrupted` with `EndedAt = StartedAt` (crediting zero duration to prevent false productivity after an unobserved crash).
- **Graceful Shutdown:**
  - Before expiry: Writes `Interrupted` with `EndedAt = max(now, StartedAt)`.
  - At or after expiry: Writes `Completed` with `EndedAt = PlannedEndAt`.

### Application Coordination
- `SessionCoordinator` serializes initialization, forwarded engine operations, and shutdown through an application-lifetime semaphore.
- Native bootstrap awaits recovery before constructing the main window and exposes the coordinator through `StartupContext`.
- Window closing is intercepted; successful shutdown performs graceful session termination before calling `Window.Close`.

### Tests & Verification
- 236 total tests passing (209 baseline + 27 Phase 3).
- Covered: empty startup, expired recovery, interrupted crash recovery, backwards clocks, graceful shutdown, duplicate recovery guards, stale recovery conflicts.
- Native smoke verified clean startup and shutdown outcomes logged under `.smoke\p3-...`.

---

## Phase 4 — Windows Background Shell

Status: **Complete, verified, and pushed**  
Date: 2026-09-06  
Branch: `native/phased-rewrite`  
Implementation commit: `d10986c`

### Objective
Wrap the application in a single-instance Windows background shell that lives in the notification area (system tray) and handles global hotkey activation.

### Architecture & Ownership
- **Single-Instance Lease (`SingleInstanceLease`):**
  - Acquires named mutex `Local\FocusKey.Shell.{WindowsUserSid}` before persistence initialization.
  - Secondary launches signal `Local\FocusKey.Shell.{...}.Activation` and exit quietly.
  - Owner listens for activation signals and shows the main window.
- **System Tray (`WindowsShellIntegration`):**
  - Hidden message window registering `Shell_NotifyIcon` (version 4 callback protocol).
  - Handles `NIN_SELECT`, `NIN_KEYSELECT`, and `TaskbarCreated` taskbar restoration.
  - Context menu with Open and Exit actions.
- **Global Hotkey:**
  - Registers `Shift + F3` with `MOD_SHIFT | MOD_NOREPEAT`.
- **Window Close-to-Hide:**
  - Closing main window hides it to tray; explicit tray/menu Exit executes `BackgroundShell.ExitAsync()`, drains shutdown, unregisters hotkeys, removes tray icon, and exits.

### Tests & Verification
- 250 total tests passing (236 baseline + 14 shell tests).
- Dedicated native probe (`ShellProbe`) verified tray icon registration, mutex acquisition, second-instance activation, hotkey conflicts (error 1409), and clean exit.

---

## Phase 5 — Quick Overlay

Status: **Complete, verified, and pushed**  
Date: 2026-09-06  
Branch: `native/phased-rewrite`  
Implementation commit: `03926b2`

### Objective
Implement the native WinUI 3 Quick Overlay triggered by `Shift + F3` to start Work (30 min) or Break (10 min) sessions.

### Implementation Details
- `QuickOverlayWindow`: Borderless, always-on-top WinUI 3 window (420 × 280 DIP reference surface), DPI-aware, centered on foreground monitor.
- Work and Break card buttons, full-width Start button, inline feedback, keyboard hints.
- Keyboard navigation: Left/Right arrows toggle Work/Break; Enter starts; Escape/outside click dismisses.
- `QuickOverlayController`: Coordinates presentation state, calls `SessionCoordinator.GetActiveAsync` and `StartAsync`.
- Active session detection: If a session is already running, displays inline status and disables Start.

### Tests & Verification
- 264 total tests passing (250 baseline + 14 overlay tests).
- Verified window reuse, Work/Break selection, keyboard controls, hotkey invocation from external foreground windows (Notepad), and clean dismissal.

---

## Phase 6 — Notifications & Completion UX

Status: **Complete, verified, and pushed**  
Date: 2026-09-06  
Branch: `native/phased-rewrite`  
Implementation commit: `7361859`

### Objective
Implement background session completion scheduling, system tray toast notifications, and completion audio cues.

### Architecture & Scheduling
- `CompletionCoordinator`: Application-lifetime scheduling adapter wrapping `SessionCoordinator`.
- Arms a one-shot `TimeProvider` timer for `PlannedEndAt - GetUtcNow()`.
- On timer wake: Invokes `CompleteIfDueAsync()`. Delayed wakes retain exact `EndedAt = PlannedEndAt`.
- Subscribes to `WM_TIMECHANGE` and `RegisterSuspendResumeNotification` to re-evaluate after sleep/resume or clock shifts.
- Submits native notification via `Shell_NotifyIcon(NIM_MODIFY)` with `NIF_INFO` ("Work session completed." / "Break session completed.") and standard notification sound with `NIIF_RESPECT_QUIET_TIME`.

### Tests & Verification
- 286 total tests passing (264 baseline + 22 Phase 6 tests).
- Verified natural timer completion, early wakeups, clock jumps, duplicate signal suppression, notification delivery, and silent Interrupted shutdown.

---

## Phase 7 — Today (Functional UI)

Status: **Complete, verified, and pushed**  
Date: 2026-09-06  
Branch: `native/phased-rewrite`  
Implementation commit: `2f72743`

### Objective
Replace the Phase 0 placeholder window with the functional native WinUI 3 Today page.

### Implemented Scope
- **Current Session Hero:** Displays active session type, remaining countdown, progress bar, and Stop button.
- **Today Summary:** Focus Time (sum of completed work duration), Break Time (sum of completed break duration), Completed Work/Break counts, and Completion Rate.
- **Activity Timeline:** Chronological list of today's sessions with local start time, type, status, and duration.
- **Local-Day Range Rules (`TodayService`):**
  - Session cohort bound by local start date.
  - Queries repository using nominal local midnight envelope $\pm 14$ hours, converting UTC timestamps using local `TimeZoneInfo`.
- **Identity-Bound Stop:** Engine `StopAsync(SessionId)` prevents stale UI from stopping a newly started replacement session.

### Tests & Verification
- 313 total tests passing (286 baseline + 27 Phase 7 tests).
- Tested midnight rollovers, non-UTC offsets (+3, -7, +14, -12), 23/25-hour DST transitions, and real UI lifecycle refresh.

---

## Phase 8 — Reports (Functional UI)

Status: **Complete, verified, and pushed**  
Date: 2026-09-07  
Branch: `native/phased-rewrite`  
Implementation commit: `b232420`

### Objective
Implement the functional native Reports dashboard with Daily, Weekly, and Monthly views, trend charts, and descriptive insights.

### Implemented Scope
- **Period Views:** Daily (24 hourly buckets), Weekly (7 day buckets), Monthly (calendar weeks clipped to month).
- **Metrics:** Total Focus Time (completed Work), Break Time, Completion Rate, Work/Break completed duration balance ratio.
- **Paired Native Trend Bars:** Work and Break duration bars with exact duration labels.
- **Focus Period Insights:** Frequency pattern analysis identifying 3-hour local focus bins with the most completed Work sessions.
- **Weekly Comparison:** Compares current week focus time to preceding calendar week with signed duration difference.
- **Timezone Resilience (`ReportsService`):** Queries half-open UTC bounds expanded by $\pm 14$ hours; local grouping preserves midnight boundaries across DST and time zone changes.

### Tests & Verification
- 347 total tests passing (313 baseline + 34 Phase 8 tests).
- Verified period aggregations, leap February, cross-year weeks, tied insight periods, and live refresh upon session completion.

---

## Phase 9 — Settings

Workspace: `the repository root`. Branch: `native/phased-rewrite`.  
Final status: **Complete, verified, and pushed** (2026-09-09).

### Sub-Phase Summary (9.1 – 9.6)

#### 9.1 — Foundation & Persistence
- Added `ApplicationSettings` model: `WorkDuration`, `BreakDuration`, `Appearance` (`System`, `Light`, `Dark`), `WorkColor` (`HexColor`), `BreakColor` (`HexColor`).
- Schema Migration 3: Created `application_settings` strict singleton table with defaults (30m Work, 10m Break, System, `#183739`, `#434763`).
- `SqliteSettingsRepository` and `SettingsService` for serialized settings management.

#### 9.2 — Runtime Durations
- `SettingsSessionDurationProvider` wires settings to `SessionEngine` and `QuickOverlayController`.
- Changing durations updates future sessions immediately without mutating active or historical records.

#### 9.3 — Runtime Appearance / Themes
- `AppearanceCoordinator` applies `System` (`ElementTheme.Default`), `Light`, or `Dark` live across `MainWindow`, title bars, and `QuickOverlayWindow`.

#### 9.4 — Runtime Work & Break Colors
- `SessionColors` provides live semantic palette updates to Quick Overlay cards, Today progress, and Reports charts.
- High-contrast black/white text calculated via sRGB relative luminance.

#### 9.5 — Functional Settings Page
- Replaced placeholder with native settings controls: minute/second numeric editors, theme ComboBox, WinUI `ColorPicker` swatches with HSV spectrum.

#### 9.6 — Auto-Save Integration & Digit Normalization
- Replaced manual Save button with automatic debounced field-level persistence.
- Added ASCII digit normalization for non-English numeral inputs (Arabic-Indic, Persian) with explicit `en-US` / `UseFlowDirection` reading order.

### Tests & Verification
- 448 total tests passing across full settings suite.
- Native UI smoke tests verified live theme switching, custom colors, auto-save persistence, and digit input normalization.

---

## Phase 10 — Mini Timer & Unified Quick Overlay UX

Workspace: `the repository root`. Branch: `native/phased-rewrite`.  
Final status: **Complete, verified, and pushed** (2026-09-09).

### Phase 10 & 10.1 Architecture
- **Phase 10 (Standalone Mini Timer):** Introduced optional compact utility window (`MiniTimerWindow`, 310 × 100 DIP) showing `Work · mm:ss` or `Break · mm:ss`, with "Keep on top" pin toggle and drag support.
- **Phase 10.1 (Unified Quick Overlay + Active Timer UX):**
  - Shift+F3 unified into the primary surface for both selection (idle) and active countdown.
  - Starting a session keeps the Quick Overlay open and transitions into the active countdown with remaining time and Stop button.
  - Reopening during an active session shows the live countdown directly.
  - Standalone Mini Timer retained as optional companion (later superseded in Phase 12).

### Tests & Verification
- 132 targeted tests passing in Phase 10; 104 targeted tests passing in Phase 10.1.
- Verified idle selection $\to$ active countdown transitions, handle reuse, Escape dismissal, and stop/completion returns.

---

## Phase 12 — Final Design Fidelity & Polish

Status: **Complete and verified**  
Date: 2026-09-10  
Branch: `native/phased-rewrite`

### Key Architectural & Design Deliverables
1. **Single Timer Presentation Architecture (Quick Overlay):**
   - Eliminated obsolete standalone `MiniTimerWindow.cs` entirely.
   - Quick Overlay (`QuickOverlayWindow`) established as the sole timer interaction and countdown surface across the application.
   - Sidebar and tray menus updated to target Quick Overlay directly.
2. **Dynamic Theme Presets & Custom Palettes:**
   - 5 curated Light presets (Default Light, Classic Blue, Forest Mint, Warm Sunset, Monochrome Gray).
   - 5 curated Dark presets (Default Dark, Midnight Navy, Forest Night, Deep Amethyst, AMOLED Pure Black).
   - Live custom hex editing for Background, Foreground, and Accent with dynamic `ThemeDictionaries` updates.
3. **Reports Polish & Streaks:**
   - Responsive chart bar widths (20–34 DIP) with dynamic `SizeChanged` adaptation.
   - Added `StreakStatistics` and `StreakCalculator`: Calculates Current Streak and Longest Streak based on qualifying completed Work days across history.
   - Integrated sleek 2-column Streaks companion card under primary summary metrics.
4. **Quick Overlay Light-Mode Styling & Deactivation:**
   - Isolated WinUI `Button` templates with custom `RootBorder` opacity states to prevent hover whiteout over semantic session colors.
   - Replaced fragile Win32 window procedure hooks with standard `Window.Activated` deactivation handling and clean 250ms grace period.

### Tests & Verification
- 504 unit tests passing (100% pass rate, 0 failed, 0 skipped).
- Verified theme switching, streaks calculation, unclipped keyboard hints, and active overlay contrast.

---

## Phase 13 — Final Branding, Packaging, Release Validation, and Feature Expansion

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Key Deliverables & Enhancements

#### 1. Branding Integration & Packaging
- Generated multi-resolution `AppIcon.ico` embedded via `<ApplicationIcon>`.
- Packaged self-contained x64 Release distribution with Inno Setup into `release\FocusKeySetup.exe` (~62.9 MB), installing to per-user `%LOCALAPPDATA%\Programs\Focus Key` without requiring UAC elevation.
- Data stored safely at `%LOCALAPPDATA%\FocusKey` across upgrades.

#### 2. Website CSV Import & Export Compatibility
- Lossless compatibility with external website focus logs (`.csv` tab-delimited):
  - Supported schemas: `date\tproject\thours` and `date\tproject\tminutes`.
  - Schema Migration 5: Added `historical_focus` table for imported aggregates.
  - Reports Integration: Contributes to Focus Time and Streaks without corrupting native session metrics (counts, break times, completion rates).
  - Transactional duplicate handling: Skips identical rows; rejects intra-file conflicting durations before write.
  - Canonical Export: Exports aggregated historical and native focus time in `date\tproject\tminutes` with deterministic `MidpointRounding.AwayFromZero`.

#### 3. Session Start & Completion Sounds (v1.1.0)
- Pure C# mathematical audio synthesis via `SoundSynthesizer.cs` (44.1 kHz 16-bit mono PCM WAV).
- Schema Migration 6: Added `session_sounds_enabled` to `application_settings`.
- Bundled sound assets: `start_tick.wav` (start cue) and `completion_bell.wav` (completion chime).

#### 4. "Start with Windows" Registry Integration
- User setting under `SYSTEM` in `SettingsView`.
- Manages `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with `"<AppPath>" --startup`.
- Quiet background launch (`--startup`): Initializes background shell, tray icon, and global hotkey without showing main window or stealing focus.

#### 5. Reports Focus Activity Chart Redesign
- Rectangular chart grid with 5-hour horizontal interval lines (`ReportsService.ComputeCeilingHours`).
- Rolling 7-day weekly window ending on anchor date (today on the right).
- Substantial focus bars (~72% column width) with duration labels above non-zero bars.
- Locale-independent Western Latin numerals (`ReportsFormatting`).
- Eliminated legacy Daily period selector.

#### 6. Partial Session Elapsed-Time Accounting
- Introduced `EffectiveDuration` on `SessionRecord`:
  - Credits actual elapsed time (`EndedAt - StartedAt`) for `Stopped` and graceful shutdown `Interrupted` sessions.
  - Keeps completion counts and rates strictly tied to `SessionStatus.Completed`.
  - Focus Time and Break Time credit all non-running sessions across Today, Reports, and CSV exports.

#### 7. Today Idle Current Session Launcher
- Redesigned idle `CURRENT SESSION` card into a unified 180 DIP hero launcher with Work and Break selection cards, duration numbers, and Start buttons.
- Schema Migration 7: Added `activity_collapsed` preference to `application_settings` with interactive chevron toggle.
- Schema Migration 8: Added `global_shortcut` preference supporting customizable hotkeys with conflict rollback.
- Curated Work (`#2F8F83` Focus Teal) and Break (`#7667B8` Calm Violet) color presets.

### Tests & Verification
- 665 automated tests passing cleanly.
- Full installer build and silent upgrade verified.

---

## Phase 14 — Settings Information Architecture & Collapsible Sections Redesign

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Architecture
Reorganized the Settings surface into a clean, hierarchical native Windows 11 configuration experience:
1. **`SESSION` (Permanently Visible Top-Level Card):** Work duration, Break duration, Session sounds master switch.
2. **`APPEARANCE` (Collapsible Top-Level Card):** Sub-cards for System Appearance (Theme, Contrast), Light Theme Palette, Dark Theme Palette, Session Colors, and Display (Time format, UI scale).
3. **`SHORTCUTS` (Collapsible Top-Level Card):** Sub-cards for Quick Overlay (`Shift + F3`) and Open Focus Key (`Shift + F4`) with conflict validation.
4. **`ADVANCED` (Collapsible Top-Level Card):** Sub-cards for Startup (Start with Windows), Quick Overlay (Reset position), and Data (Import / Export history).

### Persistence & Schema Migration 12 & 13
- **Migration 12:** Added collapsible section expansion persistence (`appearance_expanded`, `shortcuts_expanded`, `advanced_expanded` in `application_settings`).
- **Migration 13:** Added granular sound settings (`start_sound_enabled`, `completion_sound_enabled` in `application_settings`).
- Master sound switch gates playback without overwriting child states; preview buttons audition cues unconditionally.
- Prominent `"Settings"` page heading (28px SemiBold) with darkened `#363636` card borders in Dark mode.

### Tests & Verification
- 825 automated tests passing cleanly (`dotnet test -c Release`).
- Verified persistence of expansion states and granular sound preferences across restarts.

---

## Phase 15 — Application UI Scaling / Zoom System

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Architecture
Implemented a native, persistent, layout-aware Application UI Scaling / Zoom System supporting 6 discrete zoom levels:
- **80%** (0.80), **90%** (0.90), **100%** (1.00), **110%** (1.10), **125%** (1.25), **150%** (1.50).

### Interaction & Control Surfaces
1. **Global Keyboard Shortcuts:**
   - `Ctrl + Plus`: Zoom in to next discrete level (clamped at 150%).
   - `Ctrl + Minus`: Zoom out to previous discrete level (clamped at 80%).
   - `Ctrl + 0`: Reset zoom immediately to 100%.
   - TextInput suppression (guards `TextBox`, `PasswordBox`, `RichEditBox`, `AutoSuggestBox`) and AltGr modifier defense.
2. **Settings ComboBox:** `Appearance -> Display -> UI scale` with live bidirectional synchronization.
3. **Transient HUD Overlay:** Auto-fadeout `ScaleHudOverlay` pill displaying `"UI scale: X%"` for 1.5 seconds.

### Layout-Aware Scaling (No Blurry Transform)
- Effective width math: $\text{effectiveWidth} = \text{physicalWidth} / \text{scaleFactor}$.
- Responsive navigation breakpoints adapt based on effective width, shifting to Compact or Drawer layouts at higher scales.
- Proportional scaling applied to typography, timer digits, card paddings, button heights, Reports charts (`ReportsChart.cs`), and Settings controls (`_scaleUpdaters`).
- **Quick Overlay Isolation:** Quick Overlay remains strictly locked at 100% scale (480 DIP width), completely isolated from MainWindow zoom.

### Persistence & Concurrency (Migration 14)
- **Migration 14:** Added `ui_scale_percent INTEGER NOT NULL DEFAULT 100` to `application_settings`.
- Serialized through `SettingsPageController.UpdateUiScaleAsync` to eliminate concurrency races and database lock contention.

### Session Sound Behavior & Synthesis Refinement
- Start sound: Single 80ms organic confirmation tick (D5/D6/D4 harmonic) on new session start or continue after pause.
- Stop sound: Single completion chime on manual Stop or Start New from paused state.
- Natural completion: Sequential 3x repetition of 500ms C-Major chord chime (`SND_SYNC` in background thread with 180ms gap, zero audio distortion).
- Interrupted/crash: Silent.
- Sound assets 100% license-safe mathematically synthesized via `SoundSynthesizer.cs`.

### Tests & Verification
- **1,147 automated unit tests passing (1,147 passed, 0 failed, 0 skipped)** in `dotnet test -c Release`.
- Release build compiles with **0 warnings and 0 errors**.

---

## Phase 17 — Full Accessibility & Windows Narrator Audit

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Architecture
Audited and enhanced the Focus Key application so the entire core desktop experience is genuinely usable with keyboard and Windows Narrator without requiring a mouse, while strictly preserving all approved visual geometry, layouts, session state machine, WAV sound assets, and UI scaling architecture.

### Scope & Accessibility Implementations
1. **MainWindow & Navigation Shell Accessibility**:
   - Navigation items (`TodayNav`, `ReportsNav`, `SettingsNav`, `OverlayNavButton`, `ExitButton`, and all Drawer navigation buttons) configured with `ItemType="Navigation"`.
   - Dynamic context-aware accessible names on `TodayNav` & `DrawerTodayNav` reflecting live session state when active (`"Today, Work session running, 24:18 remaining"`) and `"Today"` when idle.
   - `HamburgerButton` with dynamic state announcement (`"Open navigation, Work session running"`).
   - `ScaleHudOverlay` with `AutomationProperties.Name="UI Scale"` and `AutomationProperties.LiveSetting="Polite"`.
2. **Today Page Accessibility**:
   - `WorkChoiceCard` & `BreakChoiceCard` configured with `ItemType="Radio"`, dynamic `ItemStatus` (`"Selected"` / `"Not Selected"`), and full duration details.
   - `StartIdleButton` announces target session type and duration (`"Start Work session, 25 min"`).
   - `RunningText` announces countdown and running/paused status without flooding screen readers.
   - Action buttons (`PauseButton`, `ContinueButton`, `StartNewButton`, `StopButton`) with unambiguous accessible names.
   - `TodaySummaryCard` with `Name="Daily Summary"` and dynamic accessible names on all 4 metric tiles (`Focus Time: 2h 00m`, `Work Sessions: 4 completed`, `Break Time: 30m`, `Completion Rate: 80%`).
   - `ActivityRows` with comprehensive composite accessible names (`"09:15, Work session, 25 min, Completed"`) and `ItemType="Activity record"`.
3. **Quick Overlay Accessibility**:
   - `WorkCard` and `BreakCard` configured with `ItemType="Radio"` and dynamic `ItemStatus` (`"Selected"` / `"Not Selected"`).
   - `StartButton` announces chosen mode and duration.
   - `ActiveRemaining` announces mode, remaining countdown, and running/paused state.
   - `CloseButton` with accessible name `"Close Quick Overlay"`.
4. **Reports View & Chart Accessibility**:
   - Segmented period buttons (`Week`, `Month`, `Year`) configured with `ItemType="Radio"` and dynamic `ItemStatus` (`"Selected"` / `"Not Selected"`).
   - Summary Metric cards with composite accessible descriptions combining label, value, and subtitle (`"Focus Time: 12h 30m, total focus"`).
   - `ChartCard` with high-level summary (`"Focus Activity Chart for [Period]. Total focus time: X, Y completed work sessions."`) and column containers exposing tooltips as accessible names to Narrator.
5. **Settings View Accessibility**:
   - Collapsible sections (`APPEARANCE`, `SHORTCUTS`, `ADVANCED`) configured with `ItemType="CollapsibleSection"`, dynamic `ItemStatus` (`"Expanded"` / `"Collapsed"`), accessible `HelpText` (`"Activate to collapse/expand section"`), and heading levels (`Level2`, `Level3`).
   - Granular sound controls (`_startSound`, `_completionSound`) communicate disabled state cause via `HelpText` when master sounds switch is OFF.
   - Numeric duration editors (`_workMinutes`, `_workSeconds`, `_breakMinutes`, `_breakSeconds`) configured with distinct accessible names and helper descriptions.
   - Color swatch preset buttons configured with `ItemType="Radio"`, `ItemStatus` (`"Selected"` / `"Not Selected"`), and names.
   - Shortcut buttons (`_shortcutButton`, `_mainWindowShortcutButton`) announce current combination and listening state instructions for keyboard recording.
6. **Window Close / Hide Dialog**:
   - Full keyboard accessibility and Narrator announcement via native `ContentDialog` with `PrimaryButtonText="Hide Focus Key"`, `SecondaryButtonText="Quit Focus Key"`, and `CloseButtonText="Cancel"`.

### Tests & Verification
- **1,138 automated unit tests passing (1,138 passed, 0 failed, 0 skipped)** in `dotnet test -c Release`.
- Release build compiles with **0 warnings and 0 errors**.
- All mouse-less workflows verified end-to-end.

---

## Phase 18 — Final User-Selected Sound Integration

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Architecture
Integrated the user-selected high-fidelity audio assets into the application and refined playback semantics across session lifecycle transitions and settings previews.

### Scope & Implementations
1. **User Audio Assets Integration**:
   - Integrated exact user-provided WAV assets into `src/FocusKey.App/Assets/Sounds/`:
     - `start_tick.wav` (1,263,014 bytes, 44.1kHz WAV).
     - `complete.wav` (707,772 bytes, 44.1kHz WAV).
     - `completion_bell.wav` (765,704 bytes, 44.1kHz WAV, containing 3 chimes within the file).
   - Removed synthetic runtime disk generation logic in `SoundPlayerService` (`EnsureSoundAssets`) and in test fixtures (`SoundSynthesizerTests`) to permanently preserve user-provided sound assets.
2. **Audio Playback Semantics**:
   - New session Start $\rightarrow$ plays `start_tick.wav` once.
   - Continue after Pause $\rightarrow$ plays `start_tick.wav` once.
   - Manual Stop $\rightarrow$ plays `complete.wav` once.
   - Start New (when finalizing paused session) $\rightarrow$ plays `complete.wav` once.
   - Natural timer completion $\rightarrow$ plays `completion_bell.wav` ONCE ONLY (single asynchronous Win32 `PlaySound` call; looped repeat removed because the audio asset contains three internal chimes).
   - Interrupted / crash / shutdown $\rightarrow$ completely silent.
3. **Settings Controls & Unconditional Preview**:
   - Master gate toggle (`Session sounds [On/Off]`), Start sound toggle, and Completion sound toggle preserved.
   - Preview buttons play single cue directly unconditionally (`PreviewStartTick` $\rightarrow$ `start_tick.wav` once, `PreviewCompletionBell` $\rightarrow$ `completion_bell.wav` once).

### Tests & Verification
- **1,138 automated unit tests passing (1,138 passed, 0 failed, 0 skipped)** in `dotnet test -c Release`.
- Release build compiles with **0 warnings and 0 errors**.

---

## Phase 19 — Final Pre-Release Cleanup, Consistency & Engineering QA

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Architecture
Completed application-wide engineering QA, UI consistency audit, and clean removal of temporary development flags before packaging the Release Candidate.

### Scope & Implementations
1. **Reports & Yearly Preview Cleanup**:
   - Cleanly removed `FOCUSKEY_YEARLY_PREVIEW` environment variable and its internal helper `IsYearlyPreviewEnabled()` from `ReportsView.cs`.
   - Verified exact 4 weekly buckets in Monthly reports (1–7, 8–14, 15–21, 22–end).
   - Enforced data-driven Year eligibility against real persisted session and imported history (`earliestDate.AddYears(1) <= today`).
   - Verified period calculations and chart rendering across discrete UI scaling levels (80%, 90%, 100%, 110%, 125%, 150%).
2. **UI Consistency & Accessibility**:
   - Added `ItemType="Navigation"` to `ExitButton` and `DrawerExitButton` for full automation tree consistency.
   - Verified page title hierarchy, spacing, card paddings, corner radii, borders, typography, and button heights across Today, Reports, Settings, and Quick Overlay.
   - Verified Dark, Light, Higher Contrast themes and discrete UI Scaling.
3. **Engineering QA & Runtime Smoke Test**:
   - Created `RuntimeSmokeWorkflowTests.cs` in test suite verifying full end-to-end user workflows:
     - Startup & settings bootstrap
     - Work session Start $\rightarrow$ Pause $\rightarrow$ Continue $\rightarrow$ User Stop
     - Break session Start $\rightarrow$ Natural Completion
     - Today & Reports queries across periods
     - Settings persistence (UI scale, sounds, expansions, time format)
     - App shutdown & startup recovery with paused session preservation
     - Post-restart continue and natural completion
4. **Performance & Reliability**:
   - Zero UI thread blocking.
   - Zero leaked timers, event handlers, or window resources.
   - Clean single-instance ownership and canonical exit flow.

### Tests & Verification
- **1,139 automated unit tests passing (1,139 passed, 0 failed, 0 skipped)** in `dotnet test -c Release`.
- Release build compiles with **0 warnings and 0 errors**.

---

## Phase 20 — Update Pause / Continue / Stop Sound

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Architecture
Integrated the user-selected audio asset `mixkit-select-click-1109.wav` as `session_action.wav` into the application. Configured playback for Pause, Continue, Stop, and Start New (when finalizing a paused session) while preserving the natural completion bell and start tick sound behaviors.

### Scope & Implementations
1. **User Audio Asset Integration**:
   - Integrated `session_action.wav` (195,014 bytes, 44.1kHz WAV, from `mixkit-select-click-1109.wav`) into `src/FocusKey.App/Assets/Sounds/`.
   - Cleaned temporary root copy of `mixkit-select-click-1109.wav`.
2. **Audio Playback Semantics**:
   - New session Start $\rightarrow$ plays `start_tick.wav` once.
   - Pause session $\rightarrow$ plays `session_action.wav` once.
   - Continue after Pause $\rightarrow$ plays `session_action.wav` once.
   - Manual Stop $\rightarrow$ plays `session_action.wav` once.
   - Start New (when finalizing paused session) $\rightarrow$ plays `session_action.wav` once.
   - Natural timer completion $\rightarrow$ plays `completion_bell.wav` ONCE ONLY (single asynchronous Win32 `PlaySound` call).
   - Interrupted / crash / shutdown $\rightarrow$ completely silent.
3. **Settings Controls & Gating**:
   - Automatic playback respects Master Session Sounds gate and individual sound preferences.
   - Preserved unconditional previews in Settings (`PreviewSessionAction` $\rightarrow$ `session_action.wav` once).
4. **Automated Test Coverage**:
   - Added unit tests in `SessionSoundCoordinatorTests.cs` for Pause, Continue, Stop, and Start New sound triggers.
   - Verified that disabled master and start sound settings properly gate automatic playback.

### Tests & Verification
- **1,141 automated unit tests passing (1,141 passed, 0 failed, 0 skipped)** in `dotnet test -c Release`.
- Release build compiles with **0 warnings and 0 errors**.

---

## Phase 21 — Final UI Polish Fixes & Runtime Screenshot Verification

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Architecture
Implemented low-risk native Windows 11 and Fluent Design polish fixes across the Close Dialog, Settings shortcut recording visual treatment, ColorPicker flyout placement, and Reports chart tooltip top clearance. Verified all changes against the real running application via runtime screenshot captures in both Dark and Light themes.

### Scope & Implementations
1. **Close Dialog Default Action**:
   - Explicitly configured `Hide Focus Key` as the primary default action (`DefaultButton = ContentDialogButton.Primary`).
   - Guarded `OnMainSurfacePreviewKeyDown` and `OnNavGridPreviewKeyDown` when `_isShowingCloseDialog` is active so that pressing `Enter` directly triggers "Hide Focus Key" rather than passing through to page controls.
   - Preserved all Hide, Quit, and Cancel workflows.
2. **Shortcut Recording Visual Treatment**:
   - In `SettingsView.cs`, updated `StartShortcutListening` and `StartMainWindowShortcutListening` to display `"Press keys…"` with an accent border highlight (`1.5px` `FkAccent` stroke) on the recording button.
   - Restored standard border treatment upon completion or cancellation via `Escape`.
3. **Color Picker Flyout Placement**:
   - In `SettingsView.cs`, set `Flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedRight` on all color swatch flyouts (`ConfigureColor`).
   - Ensures the color picker popover consistently opens beneath the button without covering the setting row title, description, or context.
4. **Reports Tooltip Top Clamping**:
   - In `ReportsChart.cs`, added safe top clamping (`isNearTop` detection when bar height exceeds 80% ceiling) with `PlacementMode.Bottom` and comfortable vertical offsets.
   - Preserved all underlying report calculations, data structures, and period aggregation semantics.
5. **Runtime Screenshot Verification**:
   - Verified real running application visually across Dark and Light modes:
     - `close_dialog_dark.png` & `close_dialog_light.png`
     - `shortcut_recording_dark.png` & `shortcut_recording_light.png`
     - `color_picker_flyout_dark.png` & `color_picker_flyout_light.png`
     - `reports_tooltip_top.png`

### Tests & Verification
- **1,141 automated unit tests passing (1,141 passed, 0 failed, 0 skipped)** in `dotnet test -c Release`.
- Release build compiles with **0 warnings and 0 errors**.

---

## Phase 22 — Installer & Release Candidate RC1 Packaging

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Deliverables
Packaged the complete, verified application into a self-contained Windows x64 Release distribution and Inno Setup installer (`artifacts\release\1.0.0-rc1\FocusKeySetup.exe`, 64,713,868 bytes, ~61.72 MB), verified as Focus Key Release Candidate RC1.

### Scope & Implementations
1. **Application Version Metadata Alignment**:
   - Configured `src/FocusKey.App/FocusKey.App.csproj`:
     - `<Version>1.0.0-rc.1</Version>`
     - `<AssemblyVersion>1.0.0.0</AssemblyVersion>`
     - `<FileVersion>1.0.0.0</FileVersion>`
     - `<InformationalVersion>1.0.0-rc.1</InformationalVersion>`
   - Added `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` for `Assets\AppIcon.ico` to guarantee inclusion in the published asset folder.
2. **Self-Contained Publish Configuration**:
   - `dotnet publish src/FocusKey.App/FocusKey.App.csproj -c Release -r win-x64 --self-contained true -o publish`
   - All runtime dependencies, WinUI 3 binaries, SQLite interop (`e_sqlite3.dll`), application icon (`Assets\AppIcon.ico`), and all high-fidelity sound files (`start_tick.wav`, `session_action.wav`, `complete.wav`, `completion_bell.wav`) verified in output.
3. **Inno Setup Packaging**:
   - Configured `installer.iss`:
     - `AppVersion=1.0.0-rc.1`
     - `VersionInfoVersion=1.0.0.0`
     - `OutputDir=artifacts\release\1.0.0-rc1`
   - Compiled via Inno Setup 6 (`ISCC.exe`).
   - Generates `artifacts\release\1.0.0-rc1\FocusKeySetup.exe` (64,713,868 bytes, SHA-256: `892A717FDED91CDD8B72D1077CE50AECA342EDC1F4DF6D4A0D597DF446345939`) with modern LZMA2/ultra64 solid compression.
   - Installs per-user to `%LOCALAPPDATA%\Programs\Focus Key` without requiring UAC administrator elevation.
   - Preserves user data and SQLite databases strictly isolated at `%LOCALAPPDATA%\FocusKey`.
   - Start Menu and optional Desktop shortcuts configured.
   - Registry startup entry integrated for "Start with Windows" background shell (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).

### Verification & Lifecycle QA
- **Publish**: `dotnet publish` completed cleanly with 0 errors.
- **Installer Build**: Inno Setup compiled successfully into `artifacts\release\1.0.0-rc1\FocusKeySetup.exe`.
- **Clean Silent Install**: Verified installation into `%LOCALAPPDATA%\Programs\Focus Key\FocusKey.exe`.
- **Runtime Execution**: Verified native window creation and interactive launch from installed location.
- **Single-Instance Concurrency**: Verified named mutex activation; secondary launch transfers focus to primary instance and exits cleanly.
- **Start with Windows**: Verified registry run entry `"C:\Users\...\AppData\Local\Programs\Focus Key\FocusKey.exe" --startup` without development path references.
- **In-Place Upgrade**: Verified installer running over existing installation without data loss or corruption.
- **Uninstallation & Data Safety**: Verified uninstaller removes `%LOCALAPPDATA%\Programs\Focus Key` binary files completely while leaving `%LOCALAPPDATA%\FocusKey\focus_key.db` intact.
- **Unit Tests**: 1,141/1,141 tests passing (`dotnet test -c Release`).
- **Build**: 0 warnings, 0 errors (`dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release`).

---

## Phase 23 — Focus Key v1.0.0 Final Release Promotion

Status: **Complete and verified**  
Branch: `native/phased-rewrite`

### Executive Summary & Deliverables
Promoted Focus Key from Release Candidate RC1 to the final production release (**Focus Key v1.0.0**), built the final standalone installer (`artifacts\release\1.0.0\FocusKeySetup.exe`, 64,692,688 bytes, ~61.70 MB), and verified seamless in-place upgrade over RC1.

### Scope & Implementations
1. **Version Promotion**:
   - `src/FocusKey.App/FocusKey.App.csproj`:
     - `<Version>1.0.0</Version>`
     - `<AssemblyVersion>1.0.0.0</AssemblyVersion>`
     - `<FileVersion>1.0.0.0</FileVersion>`
     - `<InformationalVersion>1.0.0</InformationalVersion>`
   - `src/FocusKey.Foundation/FocusKey.Foundation.csproj`:
     - Added `<DebugType Condition="'$(Configuration)' == 'Release'">none</DebugType>` and `<DebugSymbols Condition="'$(Configuration)' == 'Release'">false</DebugSymbols>` to ensure clean production builds with zero PDB emission.
   - `installer.iss`:
     - `AppVersion=1.0.0`
     - `VersionInfoVersion=1.0.0.0`
     - `OutputDir=artifacts\release\1.0.0`
     - `CloseApplications=force` (guarantees safe silent upgrade of background/tray instances).
2. **Clean Self-Contained Release Publish**:
   - Executed fresh `dotnet publish` targeting `win-x64` self-contained.
   - Verified zero non-production files (no PDBs, tests, source files, logs, or databases).
3. **Inno Setup Production Packaging**:
   - Compiled via Inno Setup 6 (`ISCC.exe`).
   - Generated `artifacts\release\1.0.0\FocusKeySetup.exe` (64,692,688 bytes, SHA-256: `7F3F43F160C0AC352CA0C09C3BDA95F1240A6311BDD0276F84A6395629B72089`).
   - Unsigned status reported truthfully.
4. **RC1 to Final Upgrade QA**:
   - Executed silent upgrade over existing RC1 installation.
   - Confirmed installed binary updated to `1.0.0+ae5383c...`.
   - Confirmed single `unins000.exe` and single registry uninstall key (`HKCU\...\Uninstall\{A1B2C3D4-FOCUS-KEY1-0000-000000000001}_is1`).
   - Confirmed existing SQLite database (`%LOCALAPPDATA%\FocusKey\focus_key.db`) and user settings preserved.
   - Confirmed single-instance activation and focus signaling on installed binary.
5. **Release Documentation**:
   - Created standalone [`RELEASE_NOTES.md`](file:///d:/Focus%20Key/RELEASE_NOTES.md) summarizing all features and architecture.

### Verification
- **Publish**: `dotnet publish` completed cleanly with 0 errors.
- **Installer Build**: Inno Setup compiled successfully into `artifacts\release\1.0.0\FocusKeySetup.exe`.
- **Unit Tests**: 1,141/1,141 tests passing (`dotnet test -c Release`).
- **Build**: 0 warnings, 0 errors (`dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release`).
