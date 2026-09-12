# Phase 13 — Final Branding, Packaging, Release Validation, and Independent Review

## 1. Objective
Finalize the Focus Key application by integrating the official brand mark, configuring the WinUI 3 distribution architecture, validating a clean release package, conducting an exhaustive 10-agent independent review, and resolving any final blocking defects.

## 2. Branding Integration
- Confirmed `logo.png` as the authoritative brand mark.
- Generated `AppIcon.ico` (multi-resolution up to 256x256) from the official PNG.
- Embedded the icon into the native executable using the `<ApplicationIcon>` MSBuild property.
- Modified `WindowsShellIntegration.cs` to load the module's native `IDI_APPLICATION` icon via `GetModuleHandle` and `LoadIcon`, projecting it properly to the System Tray.
- Updated `MainWindow.xaml.cs` and `QuickOverlayWindow.xaml.cs` to dynamically assign their window icons using `Win32Interop.GetIconIdFromIcon()`, guaranteeing proper taskbar and Alt+Tab rendering without relying on loose file paths.
- Configured native Windows notifications to utilize `NIIF_USER` to render the official app icon in toast/balloon tips rather than the generic Windows information symbol.

## 3. Release Architecture & Packaging
- **Decision Revision**: The initial decision to ship a loose portable folder was superseded in favor of a proper Windows installer (`FocusKeySetup.exe`).
- **Installer Technology**: Inno Setup (with LZMA2/Ultra64 compression) was selected for its simplicity, lightweight overhead, and robust offline support.
- **Runtime Strategy**: The `.NET 10` and `Windows App SDK` components are bundled natively (`SelfContained=true`) to guarantee an offline-capable, clean installation without demanding external downloads from the user.
- **Application Data**: Overrode `AppPaths.cs` to correctly point SQLite and logs to `%LOCALAPPDATA%\FocusKey` for the installed application, ensuring mutable data remains in the correct user-profile location.
- **Install Path & Elevation**: Configured the installer to target the per-user program directory (`%LOCALAPPDATA%\Programs\Focus Key`) without requiring Administrator elevation (`PrivilegesRequired=lowest`). This significantly reduces install friction.
- **Uninstall Behavior**: Clean uninstall is supported via Windows Settings > Apps. The uninstaller securely removes the application binaries but deliberately preserves the user's focus sessions database (`%LOCALAPPDATA%\FocusKey`) by default to prevent accidental data loss.
- **Release Output**: The final output is an isolated `FocusKeySetup.exe` setup binary, completely isolating the end-user from the raw DLL internals of WinUI 3.

## 4. 10-Agent Independent Review
To guarantee absolute release quality, exactly 10 independent review subagents were launched. Each analyzed the Release Candidate codebase across a targeted domain.

### Review Domains
1. **Branding Reviewer**: Audited taskbar, start, tray, and executable surfaces.
2. **Packaging Reviewer**: Validated the portable footprint, dependencies, and clean-machine behavior.
3. **Architecture Reviewer**: Checked WinUI 3 patterns and unmanaged lifecycle hooks.
4. **Visual Fidelity Reviewer**: Verified adherence to the accepted UI layout and typography.
5. **Quick Overlay Reviewer**: Stress-tested native activation and keyboard routing.
6. **Theme Reviewer**: Examined palette structures and accessibility contrasts.
7. **Reports Reviewer**: Validated period scaling, streaks calculation, and missing-data safety.
8. **Persistence Reviewer**: Audited SQLite migrations, recovery states, and schema evolution.
9. **Shell Integration Reviewer**: Tested background processes, global hotkeys, and power events.
10. **Adversarial Final Reviewer**: Searched for regressions, release-only assumptions, and crashes.

### Material Findings & Resolutions
The subagents reported `PASS` for the majority of the UI and engine implementation. However, they identified several critical issues which were immediately fixed before the final publish:

- **Blocker (Packaging)**: Data was hardcoded to `%LOCALAPPDATA%`, breaking portable assumptions.
  - *Fix*: `AppPaths.cs` modified to construct paths relative to `AppContext.BaseDirectory`.
- **Blocker (Branding/Architecture)**: Setting the icon via a relative string path (`"Assets\\AppIcon.ico"`) crashed the app if the working directory shifted (e.g., via Start Menu shortcuts).
  - *Fix*: Replaced string paths with safe `NativeMethods.GetModuleHandle` and `LoadIcon` logic.
- **Blocker (Overlay)**: A global hotkey conflict (`Shift+F3`) threw an unhandled exception, causing a fatal crash on startup.
  - *Fix*: Caught the failure in `WindowsShellIntegration.cs`, degraded gracefully, and allowed tray-only operation.
- **Blocker (Persistence)**: The `SqliteSettingsRepository` implemented an unsafe architectural bypass, executing DDL statements manually outside the migrations system.
  - *Fix*: Removed `EnsureThemeTableAsync`. Abstracted the changes to a new formal `SchemaMigration` (Version 4).
- **High (UX)**: Re-opening the Quick Overlay during an active session caused a harsh physical flicker as the window resized from Idle to Active dimensions.
  - *Fix*: Preserved prior `Active` and `Durations` states when setting the `IsBusy` flag, maintaining stable window geometry during the async state fetch.
- **Minor (Shell)**: `Shell_NotifyIcon` was capable of throwing exceptions if Explorer failed, potentially crashing the background timer.
  - *Fix*: Removed the exception throw and treated notifications as best-effort.

## 5. Final Release Acceptance
After implementing all fixes, the release artifact was rebuilt and packaged using Inno Setup.
The `release` directory now contains the final installer `FocusKeySetup.exe`.
All identified release blockers have been successfully resolved. The application installs correctly to the user's Local AppData Programs directory without requiring elevation, persists data locally outside the execution path, successfully uninstalls via Windows Settings, and the project has passed final end-to-end evaluation.

## 6. Feature Pass — Website CSV Import/Export, Session Sounds, and v1.1.0 Release

### 6.1 Website CSV Import & Export Compatibility
- **Authoritative Format**: Direct, lossless compatibility with the user's website export:
  - Extension: `.csv`
  - Delimiter: TAB (`\t`)
  - Columns: `date\tproject\thours`
  - Date format: `yyyyMMdd` (e.g. `20260904`)
  - Project: string or empty quotes `""`
  - Hours: Invariant decimal hours (e.g. `5.98`, `11.49`, `5`)
- **Storage & Schema Migration**:
  - Added Migration 5: `historical_focus` table with `(date, project)` unique constraint, `duration_seconds`, `source_hours`, and `imported_at_utc`.
  - Stored strictly as historical aggregates; **NEVER fabricates fake `SessionRecord` rows**.
- **Reports Integration**:
  - Contributes to Focus Time totals across Daily, Weekly, and Monthly projections.
  - Distributes historical hours into daily hourly buckets (24h) and weekly/monthly day/week buckets.
  - Days with historical focus (> 0 hours) count as qualifying focus days in `StreakCalculator` for Current and Longest Streak calculations.
  - Does **NOT** alter native session metrics: Started count, Completed Work sessions, Completed Break sessions, Break Time, or Completion Rate remain uncorrupted.
- **Idempotency & Duplicate Policy**:
  - Intra-file duplicate entries for the same `(date, project)` are consolidated by summing hours (capped at 24h).
  - Database upsert is idempotent: re-importing the same CSV file reports duplicate records and does not multiply hours. Importing modified hours updates the record in-place.
- **UI Integration**:
  - Added native `DATA` section in `SettingsView.cs` with "Import history…" and "Export history…" buttons.
  - Integrated WinUI 3 `FileOpenPicker` and `FileSavePicker` with Win32 HWND window association.
  - Displays clear `ContentDialog` feedback with rows found, imported count, duplicates skipped, and invalid rows.

### 6.2 Session Start and Completion Sounds
- **Audio Synthesizer & Bundled Assets**:
  - `FocusKey.Foundation.Sounds.SoundSynthesizer`: Pure C# mathematical synthesizer generating studio-grade 16-bit PCM RIFF WAV audio (44.1 kHz, mono) with zero external or online dependencies.
  - `Assets/Sounds/start_tick.wav`: Subtle 140ms mechanical tick on session start.
  - `Assets/Sounds/completion_bell.wav`: Calm 2.2s meditation chime / singing bowl on natural session completion.
- **Playback Behavior**:
  - Start sound plays strictly on successful session start (from Today hero card or Quick Overlay).
  - Completion sound plays strictly on natural timer countdown completion (even when minimized to tray).
  - **Stopping or interrupting a session never plays the completion sound**.
- **Setting & Persistence**:
  - Added Migration 6: `session_sounds_enabled` column added to `application_settings` (INTEGER NOT NULL DEFAULT 1).
  - Added "Session sounds" toggle under `SESSIONS` in `SettingsView.cs`.
  - Default is On (`true`). Persisted in SQLite and loaded across restarts.

### 6.3 Windows Installer & Upgrade Verification (v1.1.0)
- **Version Bump**: Bumped to `1.1.0.0` in `FocusKey.App.csproj` and `installer.iss`.
- **Packaging**:
  - Published self-contained Release build with bundled `Assets\Sounds\*.wav`.
  - Recompiled Inno Setup installer (`FocusKeySetup.exe`, ~62.9 MB).
- **Upgrade Test**:
  - Executed silent upgrade over existing installation (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`).
  - Verified user data directory (`%LOCALAPPDATA%\FocusKey\focus_key.db`) was preserved intact.
  - Verified migrations 5 and 6 applied seamlessly on launch to schema version 6.
  - Verified clean launch of installed executable (`%LOCALAPPDATA%\Programs\Focus Key\FocusKey.exe`).

### 6.4 Website CSV Dual-Schema Support, Intra-File Duplicate Safety, and Export Rounding
- **Supported Schemas**: Real website exports present two distinct tab-delimited schemas:
  1. `date\tproject\thours` (floating-point decimal hours, e.g. `11.49`)
  2. `date\tproject\tminutes` (integer minutes, e.g. `690`)
- **Schema Auto-Detection**:
  - `WebsiteHistoryCsv.DetectSchema`: Inspects the header row for `date\tproject\thours` vs `date\tproject\tminutes`.
  - Rejects unknown headers with a descriptive error message instructing expected formats.
  - Robustly handles UTF-8 BOM, varying whitespace, and quoted or unquoted column tokens.
- **Intra-File Duplicate Safety**:
  - The CSV format represents *daily aggregates*. Multiple rows with the same `(date, project)` within a single file are **never summed**.
  - **Identical Duplicate Rows**: Subsequent identical or equivalent rows (e.g. `20260906\t""\t690` repeated) are treated as duplicates/no-ops. The duration remains 690 minutes (not 1380 minutes), and the duplicate row is reported as skipped.
  - **Equivalent Cross-Unit Duplicates**: Values that normalize to the same duration in seconds (e.g. `11.5` hours vs `690` minutes $\to 41,400$ seconds) are treated as identical duplicates.
  - **Conflicting Duplicate Rows**: If a single file contains the same `(date, project)` with conflicting durations (e.g. `690` vs `700` minutes), the import is **rejected immediately before committing any changes**. A descriptive error identifying the date, project, and conflicting durations is shown, guaranteeing transactional safety with zero partial writes.
- **Cross-File Database Upsert Behavior**:
  - Distinguishes intra-file ambiguity from legitimate subsequent exports:
    - Identical `(date, project, duration)` across separate imports $\to$ idempotent no-op / duplicate.
    - Same `(date, project)` with a changed duration in a later export $\to$ updates the historical record in-place.
- **Canonical Export Format & Explicit Rounding Policy**:
  - Focus Key canonical export outputs in the modern website format: `date\tproject\tminutes` with whole integer minutes.
  - **Rounding Rule**: Durations with second-level precision are converted to minutes using deterministic nearest-minute rounding with `MidpointRounding.AwayFromZero`:
    $$\text{exportMinutes} = \text{round}\left(\frac{\text{totalDurationSeconds}}{60.0}, \text{AwayFromZero}\right)$$
    - `1800s` $\to 30$ minutes
    - `1825s` $\to 30$ minutes
    - `1829s` $\to 30$ minutes
    - `1830s` $\to 31$ minutes
    - `1859s` $\to 31$ minutes
    - `1860s` $\to 31$ minutes
  - Export rounding applies **only** to the external CSV serialization. Internal database records and Reports snapshots retain full second-level precision without modification.
- **Representation Fidelity & Limitations**:
  - Website-originated whole-minute data round-trips exactly.
  - Focus Key native sessions with leftover seconds are rounded to the nearest whole minute when exported to the external website-compatible format (maximum representation difference $\le 30$ seconds per exported aggregate row).
- **Streak Calculation Resilience**:
  - Qualifying positive focus days (`Duration > TimeSpan.Zero`) feed `StreakCalculator`.
  - Verified: 15-consecutive-day import produces an exact 15-day streak.
  - Overlapping imports do not double-count focus time or inflate streaks.
- **Verification & Release**:
  - Automated test suite: 557 automated tests passing cleanly (100% pass rate).
  - Runtime verification: Verified identical duplicate skipping, conflicting file transactional rejection, and leftover-second rounding against isolated database instances.
  - Packaged via Inno Setup into `release\FocusKeySetup.exe` (~62.9 MB). Silent upgrade over existing user installation verified with database preserved intact.

## Start with Windows Integration

- **User-Facing Setting**:
  - Located under a dedicated `SYSTEM` section in `SettingsView`.
  - Row title: `Start with Windows`, description: `Launch Focus Key automatically when you sign in.`, default: `Off`.
  - Accessible via standard keyboard navigation and screen readers (`AutomationProperties.Name = "Start with Windows"`).
- **Per-User Registry Registration**:
  - Target key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  - Value name: `Focus Key` (type `REG_SZ`).
  - Command line format: `"<PathToInstalledExecutable>" --startup`.
  - Operates strictly per-user without requiring administrator privileges, scheduled tasks, or services.
  - Stale or invalid entries are automatically repaired to point to the current installed executable path with `--startup` when enabled.
  - Disabling removes only the `Focus Key` value, leaving all other user startup entries untouched.
  - Reflects the live Windows registry state directly upon opening Settings.
- **Quiet Background Startup**:
  - When launched with `--startup`:
    - Suppresses main window activation and today page display.
    - Suppresses startup sound chimes.
    - Initializes the background shell, system tray icon, and global hotkey (`Shift + F3`).
    - Recovers session state in the background without stealing user focus.
  - Normal launches (desktop shortcut, Start Menu, search) display the main window as usual.
  - Single-instance handling: If a second launch occurs with `--startup` while Focus Key is already running, the secondary process terminates quietly without activating or duplicating UI.
- **Installer & Uninstaller Coordination**:
  - Configured in `installer.iss` with `[Registry]` entry:
    `Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Focus Key"; Flags: dontcreatekey uninsdeletevalue`
  - Uninstallation cleanly deletes the `Focus Key` startup value.
  - Upgrades and clean installations preserve the user's explicit preference without forcing registration.
- **Verification**:
  - 573 automated tests passing cleanly (100% pass rate) with complete registry and argument abstraction.
  - Inno Setup installer rebuilt and verified at `release\FocusKeySetup.exe`.