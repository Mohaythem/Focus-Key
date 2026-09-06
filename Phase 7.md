# Phase 7 — Today (functional UI)

Status: PASS — implementation and verification complete. Workspace: `D:\Focus Key`.
Branch: `native/phased-rewrite`.

## Scope and baseline

Baseline `00b6fdfdef59e75868dc678fa59e424526c87ae8` matched origin before coding. The full build
passed with 0 warnings/errors and all 286 tests passed. Both native smoke suites passed at
`.smoke/p7-baseline-shell-20260906` and `.smoke/p7-baseline-completion-20260906`.

The Phase 0 placeholder main window is replaced by a simple WinUI Today surface. Today is the
default on initial launch and every explicit main-window open, including tray and second-instance
activation. Reports and Settings are navigation-only destinations saying they are not implemented.
There are no report calculations, charts, settings controls, or persistence behind them.

`Focus Key.md` sections 10 and 12 define the information shown. No ZIP visual styling was copied
or refined in this phase: final design fidelity remains Phase 12 work.

## Today behavior

- Current Running session: Work/Break, timestamp-derived remaining time, progress, and Stop.
  A session that began yesterday remains visible here. A due Running row says “Finishing…”;
  observation does not complete it.
- Completed Work and Break session counts.
- Focus time and Break time: sums of actual durations of completed sessions of each type.
  Stopped, Interrupted, and Running durations are not added to completed totals.
- Completion rate: completed Work plus Break sessions divided by all sessions started today,
  including Running/Stopped/Interrupted. An empty day displays a dash rather than a misleading rate.
- Activity rows: local start time, type, status, and actual duration (planned duration while Running).
  Empty, loading, failure, and explicit Refresh states are included.

## Local-day data rules

`TodayService` reads only through `ISessionRepository`; no UI code accesses SQLite. It takes an
injectable UTC `TimeProvider` and local-zone resolver. Every read resolves the current local date
and zone. Session membership is defined by **local start date**: a session crossing midnight
belongs wholly to the date on which it started, including its completed duration. Durations are
not split across days. The independent Running query is not restricted to today's cohort.

The indexed repository range query uses a bounded UTC envelope from nominal local midnight
minus 14 hours to the following midnight plus 14 hours. Each candidate's UTC start timestamp is
then converted with the captured `TimeZoneInfo` and compared to the requested local date. This
handles non-UTC offsets and short/long DST days without assuming a day is 24 hours or assigning
an ambiguous/invalid midnight an arbitrary offset. UTC persistence is unchanged; no aggregates
or derived values are stored.

The next-day display wake uses the first valid local instant after midnight, resolving ambiguous
midnight to its earliest UTC occurrence. Tests cover transitions at midnight with 23- and 25-hour
days and both repeated local hours. Runtime dates near the .NET minimum/maximum year and unusual
historical date-line behavior are not product acceptance targets for this phase.

## Refresh and timing

`TodayController` owns navigation, loading/error state, and refresh ordering. Each refresh has a
generation; stale results cannot overwrite a later refresh, navigation, hide, or disposal. Failed
reads retain the last snapshot with an error and disable Stop until a successful refresh.

App wiring refreshes after successful Quick Overlay Start and completion. Stop refreshes after
the operation, and returning to Today or reopening the window reads fresh persisted data. Windows
clock/resume signals refresh Today and clear the cached local-zone information. Notification
submission failure does not prevent Today from refreshing a committed completion.

A one-shot display timer redraws remaining/progress once per second only while Today is visible
with a Running session. It uses `SessionSnapshot.For` and current UTC; it never decrements stored
counters or queries the database on each second. Idle Today schedules one wake at the next local
day. Hidden windows and placeholders stop the display timer and avoid Today queries. Failed reads
disable automatic display scheduling until a lifecycle/open/manual refresh succeeds.

## Stop and completed-phase integration

The existing engine gains an identity-bound Stop overload. The identity check happens inside its
existing lifecycle gate against the row just read. A stale Today snapshot therefore cannot stop
a newly started replacement session. Existing unconditional Stop callers retain their behavior.
The repository and database schema are unchanged.

`SessionCoordinator` forwards that overload. `CompletionCoordinator.StopAsync` serializes it with
Start, completion evaluation, and shutdown, retaining the engine's timing semantics. Stop before
expiry is Stopped and silent; Stop at/after expiry is Completed with `EndedAt = PlannedEndAt` and
uses the existing once-only completion feedback. Failed Stop leaves the deadline active; a conflict
does not disarm the replacement session. This is a narrow integration requirement, not another
session state machine or countdown engine.

## Files changed

- `src/FocusKey.Foundation/Today/TodayService.cs`
- `src/FocusKey.Foundation/Today/TodayController.cs`
- `src/FocusKey.Foundation/Sessions/SessionEngine.cs`
- `src/FocusKey.Foundation/Sessions/SessionCoordinator.cs`
- `src/FocusKey.Foundation/Sessions/CompletionCoordinator.cs`
- `src/FocusKey.App/MainWindow.xaml`
- `src/FocusKey.App/MainWindow.xaml.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/Startup/FoundationBootstrap.cs`
- `src/FocusKey.App/Startup/StartupContext.cs`
- `tests/FocusKey.Foundation.Tests/Today/TodayServiceTests.cs`
- `tests/FocusKey.Foundation.Tests/Today/TodayControllerTests.cs`
- `tests/FocusKey.Foundation.Tests/Today/TodayStopTests.cs`
- `README.md` and this root `Phase 7.md`

## Verification

27 new deterministic tests pass: local-day membership at offsets +3/-7/+14/-12,
midnight boundaries, totals/rates and empty data, overnight Running, read-only overdue observation,
midnight DST transitions, time-zone changes, real lifecycle refresh, cancellation, default Today,
placeholder isolation, hidden queries, refresh races/disposal/errors, duplicate Stop clicks,
identity-safe Stop, engine timing, failed Stop, and concurrent Stop versus completion.

Final restore passed. The full solution build passed with 0 warnings and 0 errors. The complete
suite passed: **313 passed, 0 failed, 0 skipped** (286 existing and 27 new tests).

Native UI interaction passed at `.smoke/p7-native-today-20260906`: initial Today/empty state,
Reports and Settings placeholders, Shift+F3, default 30-minute Work and 10-minute Break starts,
timestamp-based remaining/progress and Stop, and Stopped activity without completed-time credit.
The existing smoke probe then started isolated 10-second Work/Break sessions through the engine.
Today automatically refreshed after Work completion. Reopening from Settings via close-to-tray
and the shell Open command returned to Today with one completed Work and one completed Break,
10 seconds each, two Stopped rows, and a 50% completion rate. Persisted timestamps confirmed
natural completion used the exact planned end. The app exited gracefully.

Final native regression suites passed against the final build:
- `.smoke/p7-final-shell-20260906`: two clean launches, shell ownership, close-to-tray/open,
  competing-instance activation, graceful exit, and hotkey release.
- `.smoke/p7-final-completion-20260906`: hidden Work/Break completion, no duplicate feedback
  on repeated clock/resume signals, and silent Interrupted shutdown.

Final code review checked refresh ordering, identity-bound Stop, notification integration, and
scope. A refresh failure now disables automatic display scheduling, preventing repeated midnight
retries. Git whitespace validation passed. Reference and chat file hashes remain unchanged.

## Git delivery

Implementation commit: `2f7274396e6396949f5f487ca65441b94d6d36c0`.
Pushed successfully to `origin/native/phased-rewrite`; `git ls-remote` returned that exact SHA
on 2026-09-06. This delivery record is a documentation-only follow-up commit on the same branch.
Only the preserved, pre-existing untracked `chat_history.txt` remains outside the commits.

## Limitations and deferred scope

Layout is deliberately basic. Final spacing, typography, colors, responsive behavior, and animation
remain deferred to Phase 12. Activity currently loads the current-day cohort without paging; it is
not a Reports feature. No real Reports/Settings, charts, settings persistence, Mini Timer, startup
registration, packaging, or release behavior is implemented. Existing Active Session Overlay work
remains deferred. The default database and design/reference/chat files are preserved.

NO PHASE 8 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 7.
