# Phase 9 — Settings

Workspace: `D:\Focus Key`. Branch: `native/phased-rewrite`.

Phase 9 is in progress. This document records completed sub-phases 9.1, 9.2 and 9.3. A real
Settings UI and custom Work/Break color application are not included yet.

## Phase 9.1 — Settings Foundation & Persistence

Status: PASS — implementation and verification complete.

### Baseline and scope

Work started from Phase 8 commit `99f58b37e87d3e2c3008e7902212087501adf363`.
The local branch matched that commit; the remote baseline was verified after normal network access
was available. Phase 9.1 introduces durable settings types, validation, persistence, migration and
startup composition only. Settings remains the existing placeholder page. The session engine,
Quick Overlay, Today, Reports, appearance and colors still use their existing Phase 8 behavior.

### Model, defaults and validation

`ApplicationSettings` is the complete application configuration:

| Setting | Type | Official default |
| --- | --- | --- |
| Work duration | whole positive `TimeSpan` seconds within the UTC timestamp range | 30 minutes |
| Break duration | whole positive `TimeSpan` seconds within the UTC timestamp range | 10 minutes |
| Appearance | `System`, `Light`, or `Dark` enum | `System` |
| Work color | opaque RGB `HexColor` | `#183739` |
| Break color | opaque RGB `HexColor` | `#434763` |

`HexColor.Parse` accepts exactly `#RRGGBB` syntax and normalizes valid lowercase/mixed-case input
to uppercase. Alpha, shorthand, missing `#`, whitespace and non-hex digits are rejected. A default/
uninitialized color is invalid. Unknown enum values and zero, negative, sub-second or excessively
large durations are rejected before persistence.

### Schema, migration and persistence

Migration 3, `application_settings`, advances the database from schema 2 to schema 3. It creates
one strict `application_settings` table with these columns:

- `singleton INTEGER PRIMARY KEY CHECK (singleton = 1)`
- `work_duration_seconds INTEGER NOT NULL`
- `break_duration_seconds INTEGER NOT NULL`
- `appearance TEXT NOT NULL`
- `work_color TEXT NOT NULL`
- `break_color TEXT NOT NULL`

Database checks enforce positive durations, supported lower-case storage tokens for appearance,
and canonical uppercase six-digit RGB colors. The migration inserts exactly one row with the
official defaults in the same transaction. There are no scattered per-setting rows and no changes
to `sessions`.

`ISettingsRepository` separates settings persistence from session persistence.
`SqliteSettingsRepository` loads and atomically updates the complete singleton row through its own
short-lived connections. Save validates first and requires exactly one row to be updated; it does
not recreate a deleted row silently. `SettingsService` exposes full Load/Save and individual Work,
Break, Appearance, Work color and Break color updates. Individual and whole-record writes are
serialized within the process so they cannot lose one another through overlapping read/update work.

Bootstrap constructs this service independently beside the existing session repository and loads
the row once to validate the durable configuration. It does not pass the values into the session
engine or UI. A fresh database receives defaults only through migration 3. Existing schema-2
databases retain all session rows and receive the same default settings record.

Invalid persisted data is never silently replaced. Storage rejects invalid writes under normal
operation. If external corruption bypasses SQLite checks, repository Load throws
`InvalidDataException`, preserves the bad row for diagnosis, and startup logs/fails its normal
foundation initialization. A missing singleton row behaves the same way. Recovery/edit/reset UX
is deferred because Phase 9.1 has no Settings UI.

### Tests and verification

30 new deterministic tests use isolated SQLite database files. They cover:

- exact defaults and canonical color normalization;
- invalid color, duration and appearance values;
- fresh database schema/default row;
- schema-2 migration with existing session preservation;
- full Save/Load and persistence through a new repository after restart;
- every individual update and preservation of other fields;
- concurrent individual updates through the singleton service;
- migration/reinitialization idempotency and saved-value preservation;
- explicit handling of externally corrupted duration, appearance and color values;
- missing authoritative row, database constraints and cancellation;
- existing schema/session expectations updated only for schema version 3 and the new table.

Final verification on 2026-09-07:

- Restore: PASS; all projects up to date.
- Full solution build: PASS; 0 warnings, 0 errors.
- Complete test suite: PASS; **377 passed, 0 failed, 0 skipped** (347 existing + 30 new).
- Migration/persistence integration: PASS on real isolated SQLite databases, including schema 2 to
  3 with a pre-existing completed session, new repository/restart reads, corruption, missing-row,
  storage constraints, idempotency and concurrent database initialization.
- Native shell smoke `.smoke/p9-1-final-shell-20260907`: PASS; two clean launches against a fresh
  isolated root. Logs showed migration 3, schema version 3 and validated settings load on first and
  second startup. Tray ownership, close/open, competing instance, exit and hotkey release passed.
- Completion regression `.smoke/p9-1-final-completion-20260907`: PASS; hidden Work/Break completion,
  repeated clock/resume idempotency, notification submission and silent Interrupted shutdown.
- Normal/default user data was never opened. Only isolated test and `.smoke` roots were used.

### Files changed

- `src/FocusKey.Foundation/Settings/Appearance.cs`
- `src/FocusKey.Foundation/Settings/HexColor.cs`
- `src/FocusKey.Foundation/Settings/ApplicationSettings.cs`
- `src/FocusKey.Foundation/Settings/ISettingsRepository.cs`
- `src/FocusKey.Foundation/Settings/SqliteSettingsRepository.cs`
- `src/FocusKey.Foundation/Settings/SettingsService.cs`
- `src/FocusKey.Foundation/Data/SchemaMigration.cs`
- `src/FocusKey.App/Startup/FoundationBootstrap.cs`
- `src/FocusKey.App/Startup/StartupContext.cs`
- `tests/FocusKey.Foundation.Tests/Settings/ApplicationSettingsTests.cs`
- `tests/FocusKey.Foundation.Tests/Settings/SettingsPersistenceTests.cs`
- `tests/FocusKey.Foundation.Tests/DatabaseBootstrapperTests.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionSchemaTests.cs`
- `.smoke/run-smoke.ps1`
- `README.md` and this root `Phase 9.md`

### Limitations and Phase 9.2+ work

No settings controls, theme resources, color application, configurable Quick Overlay values or
custom session durations are wired in Phase 9.1. Later sub-phases must use this service rather than
query SQLite. Any user-facing repair/reset path for a corrupted settings row also belongs to a later
authorized sub-phase. Phase 9 itself is deliberately not marked complete.

### Git delivery

Implementation commit: `52351fcdc64d8d33e4dd863429c5ec7add3d2c2e`.
Pushed successfully to `origin/native/phased-rewrite`; `git ls-remote` returned that exact SHA on
2026-09-07. This delivery record is the required documentation-only follow-up commit on the same
branch. Only the preserved, pre-existing untracked `chat_history.txt` remains outside the commits.

PHASE 9.1 COMPLETE — PHASE 9 REMAINS IN PROGRESS.

## Phase 9.2 — Runtime Work & Break Duration Settings

Status: PASS — implementation and verification complete.

### Scope and runtime behavior

Work started from the verified Phase 9.1 delivery commit
`7da0d8aead3e7ba94a8438d7e2b67cbb847b6a2e`. Phase 9.2 connects the durable settings record to
new session creation and the Quick Overlay. It does not add Settings controls or apply appearance
or colors.

`ISessionDurationProvider` is the session-layer input boundary. The production
`SettingsSessionDurationProvider` loads the authoritative `ApplicationSettings` singleton through
`SettingsService` and maps its Work and Break values to `SessionDurations`. Bootstrap shares that
provider with the existing `SessionCoordinator`; neither the engine nor WinUI queries SQLite.

For each accepted Start, `SessionEngine` first confirms there is no Running record, then reads one
complete duration snapshot while holding its existing lifecycle gate. It writes the selected value
into the new session's immutable `PlannedDuration`. A settings update therefore affects the next
Work or Break start without an application restart. It cannot alter a Running record or a historical
record. Completion continues to use that record's `PlannedEndAt`, including delayed observation and
the existing exact `EndedAt = PlannedEndAt` rule. Repository uniqueness and atomic transition rules
are unchanged.

Fixed `SessionDurations` construction remains available for deterministic tests and smoke scenarios,
with the official 30/10 defaults as its fallback. Production application composition uses the
persisted provider. Duration load failure or cancellation occurs before `AddAsync`, so it cannot
create a partial session.

### Quick Overlay

The overlay controller requests current durations through `SessionCoordinator` together with its
active-session observation. The view receives those values in `QuickOverlayState`; reopening,
refreshing, or invoking the hotkey again after the initial load reads current persistence. Loading
uses `--:--` rather than briefly presenting stale defaults. Whole-second values render as `m:ss` or
`h:mm:ss`, and the Work/Break accessibility names receive the same live value. The old hard-coded
`30:00` and `10:00` runtime labels were removed.

### Tests and verification

13 deterministic test cases were added (8 test methods, including five formatter cases). They cover:

- fresh persisted 30-minute Work and 10-minute Break defaults;
- custom Work and Break values loaded from real SQLite after a repository/service restart;
- runtime updates observed without restarting the coordinator;
- active and historical session duration preservation across a settings change;
- exact and delayed completion using a custom duration;
- failed and cancelled duration reads leaving persistence untouched;
- overlay activation, visible refresh, repeated-hotkey refresh and exact duration formatting.

Final verification on 2026-09-07:

- Restore: PASS; all three projects restored successfully.
- Full solution build: PASS; **0 warnings, 0 errors**.
- Complete test suite: PASS; **390 passed, 0 failed, 0 skipped** (377 baseline + 13 new cases).
- Native shell smoke `.smoke/p9-2-final-shell-20260907`: PASS; two clean launches against a fresh
  isolated root, schema/settings validation, tray ownership, window hide/open, competing-instance
  handling, graceful exit and hotkey release all passed.
- Runtime duration smoke `.smoke/p9-2-shell-20260907`: PASS; settings changed to Work 47 seconds and
  Break 19 seconds while the isolated native app was running. A provider-backed Work start persisted
  exactly 47 seconds. Graceful shutdown classified it as Interrupted while retaining the original
  47-second planned duration.
- Normal/default user data was never opened. Only isolated test and `.smoke` databases were used.

### Files changed

- `src/FocusKey.Foundation/Sessions/ISessionDurationProvider.cs`
- `src/FocusKey.Foundation/Sessions/SessionEngine.cs`
- `src/FocusKey.Foundation/Sessions/SessionCoordinator.cs`
- `src/FocusKey.Foundation/Settings/SettingsSessionDurationProvider.cs`
- `src/FocusKey.Foundation/Overlay/IQuickOverlayView.cs`
- `src/FocusKey.Foundation/Overlay/QuickOverlayController.cs`
- `src/FocusKey.Foundation/Overlay/QuickOverlayDurationFormatter.cs`
- `src/FocusKey.App/Startup/FoundationBootstrap.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml.cs`
- `tests/FocusKey.Foundation.Tests/Settings/RuntimeDurationSettingsTests.cs`
- `tests/FocusKey.Foundation.Tests/Overlay/QuickOverlayControllerTests.cs`
- `.smoke/ShellProbe/Program.cs`
- this root `Phase 9.md`

### Limitations and deferred work

Phase 9.2 intentionally provides no user-facing Settings editor. Appearance mode, theme switching,
Work/Break color application, the final Settings page and visual refinement remain deferred to
Phase 9.3 and later authorized phases. The existing invalid-persistence startup failure remains
explicit; no repair/reset UI was added. No Phase 10 or later functionality was implemented.

### Git delivery

Implementation commit: `71fef3b562579677944736a999b8019031dc80a2`. Pushed successfully to
`origin/native/phased-rewrite`; `git ls-remote` returned that exact SHA on 2026-09-07. This delivery
record is the required documentation-only follow-up commit on the same branch. Only the preserved,
pre-existing untracked `chat_history.txt` remains outside the commits.

PHASE 9.2 COMPLETE — PHASE 9 REMAINS IN PROGRESS.

## Phase 9.3 — Runtime Appearance / Theme

Status: PASS — implementation and verification complete.

### Scope and architecture

Work started from the verified Phase 9.2 delivery commit
`1de30fc29f671f6c70932e4aee75431f760576f1`. Phase 9.3 makes the existing persisted Appearance
setting functional on the current native surfaces. It does not add Settings controls or apply the
persisted Work/Break colors.

`AppearanceCoordinator` is the process-wide runtime boundary over the existing `SettingsService`.
It loads the authoritative persisted appearance at startup, serializes updates and explicit
refreshes, keeps the last validated value for newly created surfaces, and publishes changes to the
application shell. It introduces no second configuration store and performs no polling.

The application refreshes appearance on normal tray/main-window and hotkey activation. This lets a
durable change made while the application is running take effect at the next existing lifecycle
boundary. An in-process `AppearanceCoordinator.UpdateAsync` publishes immediately, which is the
path a later Settings UI can use. Application event handling dispatches safely to the WinUI thread.

### System, Light and Dark semantics

- `System` maps to WinUI `ElementTheme.Default`. Theme resources therefore follow the effective
  Windows/app theme, and native title-bar color overrides are cleared so Windows owns the result.
- `Light` maps to `ElementTheme.Light` and applies a matching light native title bar.
- `Dark` maps to `ElementTheme.Dark` and applies a matching dark native title bar.

The main root covers Today, Reports and the Settings placeholder, including all controls created by
`ReportsView`. The Quick Overlay root now uses WinUI `ThemeResource` values for its surface, border,
secondary text, keycaps and control states instead of its former forced Dark root and fixed dark
surface colors. Its existing Work/Break identity colors remain unchanged for Phase 9.4. The overlay
receives the current appearance before first display, receives runtime changes while hidden or
visible, and uses the current value when reopened. Its native border also follows System, Light or
Dark.

### Tests and verification

10 deterministic test cases in eight test methods use isolated SQLite files. They cover:

- uninitialized access safety and the fresh-database System default;
- persisted System, Light and Dark startup values through a restarted repository/service;
- ordered runtime Light, Dark and System updates with durable persistence;
- explicit refresh after an external durable change;
- no duplicate notification when appearance is unchanged;
- invalid and cancelled updates preserving runtime and persistence;
- appearance updates preserving Work/Break durations and both saved colors.

Final verification on 2026-09-07:

- Restore: PASS; all projects up to date.
- Full solution build: PASS; **0 warnings, 0 errors**.
- Complete test suite: PASS; **400 passed, 0 failed, 0 skipped** (390 baseline + 10 new cases).
- Runtime appearance smoke `.smoke/p9-3-runtime-20260907`: PASS. A fresh isolated native app
  started in System, switched live to Light through main-window activation, switched live to Dark
  while creating the Quick Overlay, returned live to System, exited cleanly, then restarted from the
  same isolated database in persisted Dark. Application logs confirmed each applied value and the
  overlay's Dark creation.
- Native shell smoke `.smoke/p9-3-final-shell-20260907`: PASS; two clean isolated launches, tray,
  hide/open, competing-instance handling, exit and hotkey release passed.
- Completion regression `.smoke/p9-3-final-completion-20260907`: PASS; hidden Work/Break completion,
  repeat clock/resume idempotency, notification submission and silent Interrupted shutdown passed.
- Normal/default user data was never opened. Only isolated test and `.smoke` databases were used.

### Files changed

- `src/FocusKey.Foundation/Settings/AppearanceCoordinator.cs`
- `src/FocusKey.App/Startup/FoundationBootstrap.cs`
- `src/FocusKey.App/Startup/StartupContext.cs`
- `src/FocusKey.App/WindowAppearance.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/MainWindow.xaml`
- `src/FocusKey.App/MainWindow.xaml.cs`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml.cs`
- `tests/FocusKey.Foundation.Tests/Settings/AppearanceCoordinatorTests.cs`
- `.smoke/ShellProbe/Program.cs`
- this root `Phase 9.md`

### Limitations and deferred work

Phase 9.3 deliberately provides no user-facing setting control. Runtime smoke changes use the
isolated ShellProbe, while the coordinator's update API is ready for the later Settings page. Custom
Work/Break color application remains Phase 9.4 work. The final Settings page, final theme palette,
pixel-level styling, animation and responsive polish remain deferred to Phase 9.5 and Phase 12 as
authorized later. Mini Timer and Phase 10+ functionality were not implemented.

### Git delivery

Implementation commit: `39c6d9508431424dda7f5276bb6031d9dd2cb436`. Pushed successfully to
`origin/native/phased-rewrite`; `git ls-remote` returned that exact SHA on 2026-09-07. This delivery
record is the required documentation-only follow-up commit on the same branch. Only the preserved,
pre-existing untracked `chat_history.txt` remains outside the commits.

PHASE 9.3 COMPLETE — PHASE 9 REMAINS IN PROGRESS.

## Phase 9.4 — Runtime Work & Break Colors

### Implementation and decisions

The existing AppearanceCoordinator now publishes an immutable SessionColors snapshot alongside
appearance, from the same validated settings load. Independent change events avoid coupling theme
selection to color selection. No schema, session engine, duration provider or report calculations changed.
SettingsService and its SQLite repository remain the only persisted source of truth. Defaults remain
Work `#183739` and Break `#434763`; canonical RGB validation is unchanged.

Startup applies the loaded colors. Saving through SettingsService followed by coordinator RefreshAsync
applies runtime changes. Existing tray/open and hotkey activation boundaries perform that refresh;
external saves are observed at the next such activation, without polling or restart. Newly created
overlays receive the current snapshot, and existing hidden surfaces are updated too. Future Settings UI
must save through the service and refresh the coordinator. No Settings UI was added.

Affected native surfaces: Quick Overlay Work/Break cards and selected-type Start button; Today running
session progress; Reports Work/Break trend bars. General surfaces and text retain theme resources.
Reports recolors its cached snapshot without a database read. Colors never modify session records.

Overlay foreground is opaque black or white, whichever has greater contrast against the exact saved
RGB using sRGB relative luminance. Labels, duration text and button interaction states use this choice.
Selection uses border thickness instead of fading text. Button-state brushes are owned by the overlay
and retain identity across updates. Native verification caught and fixed an initial attempt to mutate
a shared read-only WinUI brush. The failed isolated run and logs were retained; its already-shut-down
shell process was stopped to release the executable before rebuilding.

### Tests and verification (2026-09-07)

- Restore passed; final full build: **0 warnings / 0 errors**.
- Full suite: **413 passed, 0 failed, 0 skipped** (400 baseline plus 13 color tests).
- Deterministic tests cover defaults, seven representative contrast cases, invalid default HexColor,
  unchanged refresh, separate appearance/color notifications, custom colors in all appearance modes,
  independent persisted updates, restart via a new SQLite factory, and canceled refresh.
- Native shell smoke: two clean launches, tray ownership, close-to-hide, reopen, competing launch,
  explicit exit and hotkey release passed (`.smoke/p94-shell-20260907`).
- Native color smoke passed with seeded isolated Reports data: default startup, black/white and
  dark/yellow custom pairs across Light/Dark/System, activation refresh, overlay reuse and new overlay
  after restart (`.smoke/p94-7e997023586641cb8d23336c9a309a7a`).
- Native completion smoke passed: hidden Work/Break completion, repeated signals without duplicate UX,
  silent Interrupted shutdown (`.smoke/p6-cc6428c075364422a8fe644f573f9b70`).
- Native smoke verifies successful application callbacks and shell behavior, not rendered pixels or
  manual hover/focus inspection. No visual screenshot verification is claimed.
- Final diff reviewed; protected specification, ZIP and chat history hashes remained unchanged.

### Files changed

- `src/FocusKey.Foundation/Settings/AppearanceCoordinator.cs`
- `src/FocusKey.Foundation/Settings/SessionColors.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/SessionColorBrush.cs`
- `src/FocusKey.App/MainWindow.xaml.cs`
- `src/FocusKey.App/ReportsView.cs`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml.cs`
- `tests/FocusKey.Foundation.Tests/Settings/SessionColorsTests.cs`
- `.smoke/ShellProbe/Program.cs`
- `.smoke/run-colors-smoke.ps1`
- `Phase 9.md`

### Limitations and delivery

No color picker or final Settings page; those remain for Phase 9.5. Final design, advanced palettes,
animations and polish remain deferred. Very low-contrast progress fills can blend with the background,
but their adjacent native text labels retain readable theme colors and convey the same data. No Mini
Timer or Phase 10+ work was implemented. Normal user data was not opened.

Implementation commit and verified remote SHA will be recorded in the documentation-only follow-up
after pushing this verified implementation on `native/phased-rewrite`.

PHASE 9.4 COMPLETE — PHASE 9 REMAINS IN PROGRESS.
