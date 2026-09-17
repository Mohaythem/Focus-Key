# Focus Key — Project Progress & State Checkpoint

## Current Goal
Complete Stage 4 (Settings Page & Quick Overlay Refinement).

## Current Checkpoint
Paused Session: Start New & Quick Access / Window Behavior work package is 100% complete and verified:
- **Goal 1 — Paused Session: Start New**:
  - In Paused state, provides both `[ Continue ]` and `[ Start New ]` across Today view and Quick Overlay.
  - `Continue`: Resumes the exact same session from frozen remaining time without creating a new session or mutating planned duration.
  - `Start New`: Finalizes the paused session as `Stopped` using exact active elapsed time (excluding paused wall-clock time), returns cleanly to the Work / Break launcher without auto-starting a new session, and subsequent launcher starts use the latest configured duration.
  - Invariant verified: Changing Work/Break duration in Settings never mutates already running or paused sessions.
  - Bidirectional synchronization between Today and Quick Overlay verified.
- **Goal 2A — Minimize to System Tray**:
  - Clicking titlebar Minimize button hides Focus Key directly to System Tray via Win32 subclassing (`WM_SYSCOMMAND` `SC_MINIMIZE` & `WM_SIZE` `SIZE_MINIMIZED`), leaving no taskbar item.
  - Normal window focus loss does not hide or minimize to tray. Close maintains background tray icon and shell.
  - Restores, un-minimizes, and brings window to foreground on tray icon click, second instance launch, or global hotkey.
- **Goal 2B — Dual Configurable Global Shortcuts**:
  - Default `Shift + F4`: Restores, activates, and brings main window to foreground.
  - Default `Shift + F3`: Toggles Quick Overlay.
  - Schema migration 10 adds `main_window_shortcut` to SQLite settings.
  - Settings page `SHORTCUTS` section: Independent display, capture, reset, and conflict prevention (rejects duplicate shortcuts and rolls back transactional registration on OS failure).
- **Automated Tests**: 693/693 tests pass (`dotnet test -c Release`).
- **Build**: 0 Warning(s), 0 Error(s) (`dotnet build -c Release`).

## Completed Work
- **Visual & UX Audit**: Comprehensive desktop/full-screen audit documented in `VISUAL_AUDIT.md`.
- **Visual System Definition**: Carbon Studio / Fluent design system specified in `VISUAL_SYSTEM.md`.
- **Stage 1 (Foundation)**: Design tokens, contrast corrections, typography styles, and 3-tier geometry (`b52919f`).
- **Stage 2 (Today Page)**: Responsive 880 DIP centering, idle hero launcher, active session zero-layout-jump presentation, cleaned metric titles, collapsible activity section (`7c94c90`).
- **Stage 3 (Reports Page)**: Dominant chart (73%) with secondary rail (27%), responsive narrow collapse, verified and committed (`b54c03c`).
- **Stage 4 (In Progress — Navigation Shell, Session Behavior & Window Management)**:
  - Responsive navigation sidebar across Narrow (171 DIP), Restored (229 DIP), Maximized (272 DIP).
  - Destructive Stop interaction replaced with non-destructive Pause / Continue / Start New lifecycle.
  - Minimize to tray with single-instance activation and global hotkey restoration.
  - Dual global shortcuts (`Shift + F3` Quick Overlay, `Shift + F4` Open Focus Key) with SQLite persistence and conflict management.

## Remaining Work
- **Stage 4: Settings Page & Quick Overlay Refinement**
  - Settings Page: Constrain container to 880 DIP max-width, center on desktop, expand color preset click targets (36x36 DIP), clean microcopy echoes.
  - Quick Overlay: Clean floating card hierarchy, standardize Stop action, harmonize keyboard hints (`[← →]`, `[Enter]`, `[Esc]`).
- **Stage 5: Final Consistency QA & Windows Packaging**
  - Full-surface visual regression audit (Maximized, Restored, Minimum; Dark, Light, High Contrast).
  - Package clean standalone Release installer (`FocusKeySetup.exe`).

## Important Active Decisions
- **Architecture**: Single authoritative session engine pipeline (`App.xaml.cs` -> `CompletionCoordinator` -> `SessionCoordinator` -> `SessionEngine`). Zero duplicate timers or secondary state.
- **Window Management**: Win32 window subclassing (`SetWindowSubclass`) intercepts `SC_MINIMIZE` and `SIZE_MINIMIZED` to hide to tray before standard minimize animation creates taskbar clutter. Single-instance named pipe signaling wakes and restores foreground.
- **Global Hotkeys**: Win32 `RegisterHotKey` managed via `WindowsShellIntegration` with atomic transactional rollback if registration fails. Duplicate shortcuts rejected with validation feedback in Settings.
- **Desktop Sizing**: Dedicated content width tiers (`880 DIP` for Today/Settings, `1220 DIP` for Reports). Never stretch unbounded across 1600+ DIP desktop viewports.
- **Culture Invariance**: Strict `CultureInfo.InvariantCulture`, Western Latin digits (`0-9`), and Gregorian calendar enforcement.
- **Git Workflow**: Commit and push only when an entire stage/work package is complete, verified, and ready as one coherent unit.

## Last Verification
- **Build**: `dotnet build -c Release` (0 Warnings, 0 Errors).
- **Test Suite**: `dotnet test -c Release` (693 passed, 0 failed, 0 skipped).
- **Runtime Verification**: Subclass minimize-to-tray, single-instance restoration, Start New lifecycle, and dual hotkey Settings UI verified via real runtime execution and screenshots.

## Last Commit
`b54c03c` — `refactor(reports): redesign wide desktop layout with dominant chart and secondary rail` on branch `native/phased-rewrite`.

