# Phase 2 — Session Engine

Status: implementation and verification PASS; Git checkpoint recorded below.
Date: 2026-09-05
Workspace: `D:\Focus Key`
Branch: `native/phased-rewrite` (not renamed)
Preceding commit: `294218513034e4f00628842f3624d889b31f60a7`

## Objective and implemented scope

Complete the four unfinished local engine files without importing Phase 3 work. Focus Key can
now start Work or Break through a UI-independent API, observe the active session, stop it, and
explicitly complete it when due. SQLite remains the only durable session state. Nothing wires the
engine into application startup or changes the placeholder UI.

The existing draft's timestamps, injectable `TimeProvider`, immutable durations/snapshots/outcomes,
and per-instance lifecycle gate were retained. Its unconditional terminal writes were replaced by
atomic repository comparisons. No packages, projects, schema versions, or migrations were added.

## API

`SessionEngine(ISessionRepository, TimeProvider? = null, SessionDurations? = null)` uses
`TimeProvider.System` and the product durations by default.

| Operation | Result and effects |
| --- | --- |
| `StartAsync(SessionType, CancellationToken)` | Validates type/duration/end range, reads active state, creates a stable ID, inserts Running immediately, and returns the persisted record. Rejects an existing Running session, including one past its deadline. |
| `GetActiveAsync(CancellationToken)` | Returns a snapshot of the stored Running session, or null. Never updates persistence, even after expiry. |
| `StopAsync(CancellationToken)` | Returns NoActiveSession, Stopped, Completed, or Conflict. |
| `CompleteIfDueAsync(CancellationToken)` | Returns NoActiveSession, StillRunning, Completed, or Conflict. |

`SessionOutcome.ChangedStoredState` is true only for a successful Stopped or Completed result.
For Conflict, `Session` contains the originally observed record, explicitly not current database
state. The caller can observe again; the engine never silently retries a lifecycle action.

`SessionSnapshot` includes identity, type, start, planned duration/end, observation instant,
elapsed, remaining, and `HasReachedPlannedEnd`. Its factory requires a valid Running record.

## Lifecycle and time semantics

- Work defaults to 30 minutes; Break defaults to 10 minutes. A caller may supply other positive,
  whole-second durations. This is configuration supplied to the engine, not Settings persistence.
- `PlannedEndAt = StartedAt + PlannedDuration`.
- `Remaining = max(PlannedEndAt - ObservedAt, 0)`.
- `Elapsed = clamp(ObservedAt - StartedAt, 0, PlannedDuration)`.
- Before start (e.g. a backward clock adjustment), elapsed is zero; remaining can exceed the
  planned duration, as required by the timestamp formula.
- Stop before the deadline writes Stopped with the observed UTC time. If the clock is before the
  start, its end is clamped to StartedAt so stored actual duration cannot be negative.
- Stop at or after the deadline writes Completed. Explicit natural completion does the same.
  In both cases `EndedAt = PlannedEndAt`, including a completion evaluated hours late.
- Observation after expiry still describes Running with zero remaining. It never performs
  completion. There is no timer scheduler, countdown loop, or completion callback in this phase.
- No operation creates Interrupted or classifies a session as stale. Phase 3 owns recovery policy.

UTC normalization and tick-exact persistence remain unchanged. A duration that cannot fit anywhere
in the UTC timestamp range is rejected by `SessionDurations`. A duration that does not fit after a
particular start instant is rejected by `SessionRecord.Validate` before any insert or update.
Stored seconds are converted to ticks with checked integer arithmetic on read; reconstructed
records are validated, so malformed/extreme stored values fail loudly instead of creating an
active session whose planned end later overflows. No new arbitrary product duration maximum was
invented.

## Concurrency and persistence decisions

The draft's `SemaphoreSlim` serializes lifecycle operations on one engine instance. It cannot
protect different engine instances or processes. The existing unique Running index correctly
protects concurrent inserts, but it cannot prevent two readers from later overwriting each other's
terminal outcome.

The narrow Phase 1 addition is:

`Task<bool> TryUpdateAsync(SessionRecord expected, SessionRecord replacement, CancellationToken)`

Both records must validate and share identity. One SQL UPDATE compares every stored fact in its
WHERE clause: ID, type, status, start, planned seconds, nullable end, and creation timestamp.
It executes in an immediate transaction. Exactly matching storage returns true; a changed or
missing row returns false. SQLite errors propagate. Comparing the complete record also prevents
completing an old deadline after another writer changes the duration. No revision column or schema
migration is necessary.

All engine terminal writes use this operation. Two readers of the same Running record can yield
only one successful transition. The loser returns Conflict without retrying, which also prevents
an old Stop request from stopping a new session started after the original one finished. Sequential
completion calls find no Running row after the first transition and return NoActiveSession.

The existing raw `UpdateAsync` remains available under its Phase 1 contract; it deliberately does
not enforce lifecycle transitions. Engine clients must use the engine API for lifecycle behavior.
Arbitrary external SQL or unconditional raw repository writes are outside the engine's guarantee.

The engine caches no active record, holds no shared database connection, and uses no global lock.
Observations are point-in-time snapshots and can become stale immediately after a concurrent write.

## Cancellation and failures

- A cancelled request cannot acquire the lifecycle gate. A cancelled queued request never runs.
- Repository cancellation is passed through reads and writes. Validation and storage failures
  propagate; gate release is guaranteed by finally blocks.
- Add, raw Update, and conditional Update check cancellation immediately before committing their
  transaction. Earlier failures/cancellation roll back via disposal.
- Once that check passes, commit runs synchronously without a cancellation token. There is no
  cancellation check after a successful commit: a committed transition returns success even if
  cancellation arrives immediately afterwards. Cancellation is not an undo operation.
- SQLite's provider performs synchronous I/O despite its async API. Cancellation is cooperative;
  it cannot promise to interrupt an already executing synchronous SQLite call or commit instantly.
- No retry loop masks storage failures. A later explicit request re-reads SQLite and can retry a
  failed transition if it remains applicable.

## Tests and review

Full suite: **209 total, 209 passed, 0 failed, 0 skipped**. The previous 154 cases remain intact;
55 new cases cover the engine and the necessary data-layer corrections.

Coverage includes Work/Break defaults and custom durations; invalid types/durations and end
overflow; read-only snapshots and time clamps; no-active results; stop before/at/after deadline;
exact and delayed natural completion; repeated completion; persistence failure and retry; gate
release after read failure; cancellation before requests, while queued, before persistence, and
after successful commit; and actual SQLite write failure/rollback through an injected test trigger.

Cross-instance integration tests use independent repositories on a real isolated SQLite file.
TaskCompletionSource barriers force both engines to finish reading the same original state before
either can write. They prove competing starts, stop versus completion, competing completion, and
an old Stop request losing after a different engine finishes the original and starts a new session.
Other regression tests prove every stored field participates in the comparison, stale updates do
not overwrite terminal state, missing rows return false, and corrupt extreme durations fail on read.

There are no sleeps or wall-clock delays in the new unit/integration tests. Manual clocks control
all engine time. The native smoke helper necessarily waits for process/window readiness with bounded
timeouts; it is not used to validate timer timing.

The primary author selected and implemented the concurrency contract, reviewed Luna's routine test
and tooling contributions, corrected duration-boundary tests and per-run smoke assertions, reviewed
the final source/diff, and executed final verification. No existing tests were weakened.

## Files created / brought under version control

- `src/FocusKey.Foundation/Sessions/SessionEngine.cs` — reviewed and repaired existing untracked draft.
- `src/FocusKey.Foundation/Sessions/SessionDurations.cs` — retained draft with range validation.
- `src/FocusKey.Foundation/Sessions/SessionSnapshot.cs` — retained draft with active-record validation.
- `src/FocusKey.Foundation/Sessions/SessionOutcome.cs` — retained draft with explicit Conflict outcome.
- `tests/FocusKey.Foundation.Tests/Sessions/ManualTimeProvider.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/EngineTestRepository.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionEngineTests.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionDurationsTests.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionSnapshotTests.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionConcurrencyTests.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionAtomicUpdateTests.cs`
- `.smoke/run-smoke.ps1` — corrected existing ignored helper, now tracked for reproducibility.
- `Phase 2.md`

## Existing tracked files modified

- `src/FocusKey.Foundation/Sessions/ISessionRepository.cs` — conditional update contract.
- `src/FocusKey.Foundation/Sessions/SqliteSessionRepository.cs` — atomic comparison, explicit commit
  cancellation boundary, checked duration reading and record validation.
- `src/FocusKey.Foundation/Sessions/SessionRecord.cs` — representable planned-end invariant.
- `README.md` — links to phase records at the repository root.

The specification, design ZIP, Phase 0/1 records, application UI, migration list, dependencies, and
`chat_history.txt` were not modified. Chat history remains untracked. Build/runtime artifacts remain
ignored; only the smoke script itself is intentionally added from `.smoke`.

## Verification commands and results

Run from `D:\Focus Key`:

```powershell
dotnet restore FocusKey.slnx
dotnet build FocusKey.slnx -c Debug --no-restore
$phase2Temp = Join-Path (Get-Location).Path '.smoke\test-temp'
New-Item -ItemType Directory -Force -Path $phase2Temp | Out-Null
$env:TEMP = $phase2Temp
$env:TMP = $phase2Temp
dotnet test FocusKey.slnx -c Debug --no-build --no-restore
powershell.exe -NoProfile -File '.smoke\run-smoke.ps1'
git --no-optional-locks diff --check
git --no-optional-locks status --short --branch
```

The TEMP/TMP overrides apply only to the verification shell and its child test host. They keep the
existing suite's temporary databases under the workspace. The default local user database was
never opened or changed.

Restore: PASS. Initial sandbox attempt could not reach NuGet (NU1301); the same restore succeeded
with approved network access. No package versions changed.

Build: PASS, **0 warnings, 0 errors**. A first development build caught a test wrapper not yet
implementing the new repository member; it was fixed before the successful build and full test run.

Tests: `Passed! - Failed: 0, Passed: 209, Skipped: 0, Total: 209`, duration 2 seconds.

Native smoke: PASS outside the sandbox. The first restricted launch displayed a host startup error
before application initialization. It was not counted as a pass; its exact test process was closed.
The same executable/script then passed with approved native execution:

- Isolated root: `D:\Focus Key\.smoke\p2-9ddaff509a0742a4bd42bf952f185cbf`.
- First process 16768: responsive `Focus Key` window, migrations 1 and 2 applied, schema 2,
  foundation initialized, placeholder displayed, clean shutdown, exit 0.
- Second process 26628: responsive window, schema 2, no migrations applied, clean shutdown, exit 0.
- Database header independently reports schema 2 after shutdown.

The smoke helper derives the workspace from its own path (no old `D:\Focus Key NEW` dependency),
requires a fresh contained data root, validates only each run's appended log text, checks both
schema migrations and second-launch idempotency, and restores the caller's data-root environment.

Reference Git blob hashes still match HEAD:

- `Focus Key.md`: `8d13266f6fed15cb70c67906776c321a72fc83aa`
- `Focus Key.zip`: `6c89864281ce81507b2dbeed99c9a0ca54bd4d5f`
- Unchanged local `chat_history.txt` hash: `80b09f5a0a6fcea33f2f185f849ea7a4f25894aa`

## Git checkpoint

Branch remains `native/phased-rewrite`; origin remains `https://github.com/Mohaythem/Focus-Key.git`.
The implementation commit and verified remote result will be recorded in a documentation follow-up,
because a commit cannot contain its own SHA. No rename, merge, force push, or history rewrite.

## Limitations and deferred scope

This phase exposes engine operations, not an automatically scheduled timer or usable focus UI.
It does not wire an engine into app startup or make a closed window keep a session running.
No recovery/classification, application lifecycle coordination, sleep/resume handling, single
instance, tray, hotkey, overlays, notifications, sounds, Today, Reports, Settings, Mini Timer,
startup registration, or packaging/release functionality was added.

Wall-clock changes affect timestamp-derived observations; the basic negative-time safeguards here
do not constitute a sleep/resume or clock-change policy. Recovery remains Phase 3. Existing raw
repository writes remain general-purpose and must not be used to bypass engine lifecycle semantics.
No production database was used for tests or smoke verification.

NO PHASE 3 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 2.
