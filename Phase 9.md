# Phase 9 — Settings

Workspace: `D:\Focus Key`. Branch: `native/phased-rewrite`.

Phase 9 is in progress. This document currently records Phase 9.1 only; no Phase 9.2 behavior or
real Settings UI is included.

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
custom session durations are wired yet. Phase 9.2 must build the real Settings UI and decide when
saved values take effect while preserving active-session timestamps. It must use this service rather
than query SQLite. Any user-facing repair/reset path for a corrupted settings row also belongs to a
later authorized sub-phase. Phase 9 itself is deliberately not marked complete.

### Git delivery

Implementation commit: `52351fcdc64d8d33e4dd863429c5ec7add3d2c2e`.
Pushed successfully to `origin/native/phased-rewrite`; `git ls-remote` returned that exact SHA on
2026-09-07. This delivery record is the required documentation-only follow-up commit on the same
branch. Only the preserved, pre-existing untracked `chat_history.txt` remains outside the commits.

PHASE 9.1 COMPLETE — PHASE 9 REMAINS IN PROGRESS.
