# Phase 3 — Recovery & Application Coordination

Implemented and verified on 2026-09-05 on `native/phased-rewrite`.

NO PHASE 4 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 3.

## Recovery rules

Persistence initializes before startup recovery reads the existing Running row. No row means no write. At or after `PlannedEndAt`, recovery writes Completed with `EndedAt = PlannedEndAt`, even when observed much later. Before that deadline, startup writes Interrupted with `EndedAt = StartedAt`.

The interruption timestamp deliberately credits zero duration: neither the crash instant nor the last moment the previous process was alive is known. Restart time would invent productive duration. Identity, type, start, planned duration and creation timestamp remain unchanged. A clock before StartedAt also produces a valid zero-duration interruption. UTC comes from an injectable `TimeProvider`; there are no countdown counters or recovery timers.

Graceful shutdown has a known observation time. Before expiry it writes Interrupted with `EndedAt = max(now, StartedAt)`; at/after expiry it writes Completed at the planned end. Explicit user Stop remains Phase 2's Stopped behavior. Unexpected termination cannot reliably write anything; the next startup applies the recovery rules above. This can undercount an interrupted session, intentionally.

## Coordination and persistence

`SessionRecovery` performs one read and at most one conditional transition. `SessionCoordinator` owns a private Phase 2 engine and recovery service, and serializes initialization, forwarded engine operations and shutdown with one application-lifetime semaphore. Normal operations require successful initialization. Repeated initialization returns its original historical result, so it cannot interrupt a session subsequently started through that coordinator. Repeated successful shutdown returns its original result and normal operations are then rejected.

SQLite remains the durable source of truth. Only startup/shutdown operation results are cached; active state is always queried through the engine. Failures and cancellation before a successful outcome are not cached and allow explicit retry. Cancellation while queued does not execute the operation. Cancellation observed after reading aborts before writing. A successful commit remains successful even if cancellation arrives immediately afterward.

Recovery reuses Phase 2 `TryUpdateAsync(expected, replacement)`, which atomically compares every persisted field. Independent coordinators/repositories that observed the same row have only one winner. A stale writer returns Conflict without retrying, cannot overwrite a terminal state or edited Running record, and cannot touch a replacement session. Conflict is a completed attempt, not a claim that its historical Session payload is current; callers can query active state separately.

No schema, migration, repository, domain record, or Phase 2 engine changes were necessary. Schema remains version 2, including the unique Running index. Existing Phase 0–2 tests still pass.

The native bootstrap awaits recovery before constructing the placeholder window and exposes the coordinator through StartupContext. Window closing is cancelled while shutdown persistence runs. Successful shutdown queues WinUI Window.Close after the closing event unwinds. Repeated close requests are coalesced; persistence failure is logged, reported, and leaves the window open for retry. This is a minimal adapter, not a background application shell.

## Files changed

- Added `src/FocusKey.Foundation/Sessions/SessionRecovery.cs`, `SessionRecoveryResult.cs`, `SessionCoordinator.cs`.
- Updated `src/FocusKey.App/App.xaml.cs`, `src/FocusKey.App/Startup/FoundationBootstrap.cs`, `StartupContext.cs`, `FatalError.cs`.
- Added `tests/FocusKey.Foundation.Tests/Sessions/SessionRecoveryTests.cs`, `SessionCoordinatorTests.cs`, `SessionLifecycleRaceTests.cs`, `LifecycleTestRepository.cs`.
- Updated `.smoke/run-smoke.ps1` to check startup and shutdown coordination logs and use Phase 3 isolated roots.
- Updated README root phase links and added this document.

Astra defined the semantics, authored recovery/coordinator and concurrency tests, reviewed Luna's bounded app integration and ordinary tests, and performed final verification. Review corrected an AppWindow/Window API mistake before acceptance. An initial test expected one repository read across initialization and Start; inspection confirmed Start's existing guard adds a second read, and the assertion now counts both while still detecting repeated recovery.

## Tests and verification

Final result: **236 passed, 0 failed, 0 skipped** (209 existing plus 27 Phase 3 cases).

Deterministic tests cover empty startup; before/exactly/after expiry; planned-end completion; conservative interruption; backwards clocks; clean shutdown; repeated initialization/recovery/shutdown; all pre-init and post-shutdown operation guards; forwarded engine behavior; independent-instance recovery races; stale recovery versus completion/new session/edited Running fields; queued Start/shutdown ordering; cancellation while queued, after read and after commit; failure retry; and real SQLite trigger-induced rollback. TaskCompletionSource barriers control interleavings without sleeps or wall-clock waiting.

Commands run from `the repository root`:

```powershell
dotnet restore FocusKey.slnx
dotnet build FocusKey.slnx -c Debug --no-restore
$env:TEMP=Join-Path (Get-Location).Path '.smoke\test-temp'
$env:TMP=$env:TEMP
dotnet test FocusKey.slnx -c Debug --no-build --no-restore
powershell.exe -NoProfile -File '.smoke\run-smoke.ps1'
git diff --check
git diff --stat
git status --short
```

Restore succeeded (all projects up to date). Full build succeeded with **0 warnings and 0 errors**. Complete test suite passed. Native smoke passed two responsive launches and exit-code-0 shutdowns under `.smoke\p3-1e22102edc26424e94acb36ed6951bdf`; first launch applied migrations 1 and 2, second applied none, schema remained 2, and both logged NoActiveSession startup/shutdown outcomes. Smoke uses bounded polling for native window readiness, separate from deterministic tests. Recovery of populated databases is covered by real SQLite tests; the native smoke uses an empty isolated database.

Final source/diff review found no remaining Phase 3 correctness issue. `chat_history.txt`, specification and design ZIP hashes remain unchanged. The default/legacy local database was not opened. Smoke artifacts are ignored and not committed.

## Limitations and deferred work

Startup treats a Running row as leftover under the requested policy. There is no process ownership/liveness detector: concurrently live application lifetimes are not excluded. Conditional updates protect stale observations, but do not establish ownership of sessions another live instance starts. Phase 4 single-instance enforcement is explicitly deferred; this phase does not claim to solve it.

There is no heartbeat, crash-time inference, sleep/resume handling, or periodic completion scheduler. A forced process exit bypasses graceful shutdown and is handled on restart. The placeholder UI does not expose Start controls yet. Shutdown error dialog behavior was reviewed; automated failure injection exercises the UI-independent coordinator rather than native modal interaction.

Single instance, tray, hotkey, overlays, notifications/sounds, Today, Reports, Settings, Mini Timer, startup registration, packaging and all Phase 4+ product work remain deferred.

## Git delivery

Implementation commit: `a7272c106dcffe6408c3213904b4bf1e28fc8bcb` (`Implement Phase 3 recovery and application coordination`). `git push origin native/phased-rewrite` succeeded. `git ls-remote origin refs/heads/native/phased-rewrite` returned the identical full SHA, verifying remote delivery. This delivery record is a documentation-only follow-up commit. The branch name is unchanged; no force push or history rewrite was used. The only intentionally untracked user file is the unchanged `chat_history.txt`.
