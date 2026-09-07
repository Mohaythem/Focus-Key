# Phase 8 — Reports (functional UI)

Workspace: `D:\Focus Key`. Branch: `native/phased-rewrite`.
Status: PASS — implementation and verification complete.

## Baseline and scope

Started from Phase 7 commit `3a1874d52d9488bb676359a5414157bf1ee77033`; origin matched that SHA.
The pre-existing untracked `chat_history.txt` is preserved. Product requirements come from
`Focus Key.md` sections 11–13 and 21. The ZIP Reports component was read for structural reference:
Daily/Weekly/Monthly selection, metrics, time trends and descriptive insights. Its mock numbers
are not used. Reports is functional native WinUI; Settings remains a placeholder and Today
remains the initial/reopened page. No session-engine or database-schema changes were needed.

## Reports and calculations

- Daily, Weekly (default), Monthly; date picker, Previous, Next, Current period, manual Refresh.
- Focus time = sum of actual durations of **Completed Work** sessions.
- Completed Work/Break counts, started counts by type, completed Break duration.
- Completion rate = all Completed Work + Break / all sessions started in the selected period.
  Running, Stopped and Interrupted remain in the denominator; their durations never enter completed
  time. Empty data displays a dash for the rate and balance, zero counts/durations, and an empty message.
- Separate Stopped, Interrupted and Running counts make the denominator visible.
- Work/Break balance = each type's share of completed duration, with no score or judgement.
- Simple paired native bars with exact duration labels: Daily groups into 24 local start hours;
  Weekly into seven local dates; Monthly into Monday–Sunday calendar weeks clipped to the month.
  Zero buckets are retained. Both bars use the same scale for the view. Repeated DST hours combine
  into the same wall-clock hour; skipped hours are zero.
- Focus-period pattern counts completed Work starts in fixed three-hour local buckets. All ties
  are displayed, with counts; no-data reports make no pattern claim. This is descriptive frequency,
  not a claim about ability, a statistical prediction, or coaching.
- Weekly comparison shows completed focus time for the week containing the selected date versus
  the preceding full calendar week, and the signed duration difference. Both exact ranges and the
  incomplete-current-week caveat are shown. In Monthly view this comparison remains explicitly
  about the selected date's week, while monthly totals stay restricted to the month.

## Local dates and range semantics

All persisted timestamps remain UTC. Like Today, membership uses **local start date**; a session
crossing midnight belongs wholly to the start date. No prorating or persisted aggregates are added.
Weeks start Monday, consistent with the reference. Date ranges are half-open internally.

`ReportsService` resolves the injected local zone and UTC time per read. It makes one repository
range query spanning the selected period and both comparison weeks, expanded by +/-14 hours.
Actual UTC-to-local conversion then decides membership. This handles non-UTC offsets, DST and
ambiguous/invalid midnight without assuming 24-hour local days. All calculations in one report
use the same returned row set. Monthly bins do not include adjacent-month sessions.

The supported date picker/calculation range is 0002-01-08 through 9998-12-24, reserving room for
comparison weeks, whole months and UTC envelopes. Boundary navigation is a no-op rather than an
overflow. Extreme bounds are tested; normal calendar dates are the product use case.

## Architecture and refresh

`ReportsService` and `ReportTotals` in Foundation own calculations. `ReportsController` owns
selection, loading/error state and refresh generation. WinUI performs formatting only; it never
queries SQLite. Bootstrap shares the existing repository with Reports. Reads run through a
background task in the native adapter so synchronous SQLite work does not block the UI thread.

Opening Reports, changing period/date, Previous/Next/Current, manual refresh, session Start/Stop/
completion, and existing clock/resume signals refresh the visible page. Hidden Reports do not
query; reentry reads fresh data. Historical selections persist during the process. Current period
follows the injected current date on subsequent refreshes; explicit selection pins that date.
There is no database polling or report countdown. An idle open report retains its labelled range
until one of those refresh triggers; there is no additional midnight polling or lifecycle machinery.

Each refresh clears old metrics while loading; stale results/errors cannot overwrite a newer range
or a hidden/disposed page. Failures show an actionable Refresh message and no mismatched old totals.
Today's existing display timer and lifecycle authority are preserved. App refresh routing now
updates the visible Today/Reports surface, including when a Stop finishes after navigation.
Completion-notification failure still cannot prevent refresh of a committed session.

## Tests and native verification

34 new deterministic tests use isolated real SQLite stores for calculations/lifecycle behavior,
plus controlled asynchronous completions for controller races. Coverage includes Work/Break status
totals, rates/balance, local +3/-7/+14/-12-hour boundaries, overnight attribution, 23/25-hour DST days,
leap February, cross-year weeks, clipped monthly bins, comparison exclusions, empty reports, tied
focus periods, time-zone changes, overdue observation without mutation, cancellation, engine
Start/Stop/Complete refresh, date navigation/limits, errors/retry, hide/dispose, and stale results.
All existing tests remain unchanged. Full suite currently passes: **347 passed, 0 failed, 0 skipped**
(313 existing + 34 new). The initial restricted restore could not reach NuGet; normal restore with
network access succeeded without bypassing sources or vulnerability checks.

Native fixture: `.smoke/p8-native-reports-20260907`, created by the new guarded `seed-reports`
probe command. It refuses an existing directory and is restricted to `.smoke` children. Fixture
creation goes through the existing repository, never the default database.

Observed via native controls and accessibility/screenshot inspection:
- Today remained default and matched the fixture.
- Weekly: 30m Work, 10m Break, one completed each, one Stopped, one Interrupted, 50% completion,
  75%/25% completed time balance. Previous week correctly showed 2h 30m, including both fixture rows.
- Monthly selection, clipped week bins, Previous and Current controls returned correct ranges/totals.
- Daily showed hourly buckets; selecting September 2 in the calendar showed empty metrics; Next
  moved to September 3. Current returned to September 7.
- A real probe-started 10-second Work session naturally completed while Reports was visible:
  focus became 30m 10s and completion 60%, without manual refresh.
- Shift+F3 and a default Work start updated Reports to Running 1 without increasing completed time.
  Today showed remaining time and Stop. Stop persisted Stopped; returning to Reports showed
  Stopped 2, Running 0, unchanged 30m 10s focus, and 50% completion.
- Close-to-tray and shell Open returned to Today. Settings displayed only its placeholder.
  The isolated app exited gracefully.

Final verification on 2026-09-07:
- Restore: PASS, all projects up to date.
- Full solution build: PASS, 0 warnings, 0 errors.
- Complete test suite: PASS, 347 passed, 0 failed, 0 skipped.
- `.smoke/p8-final-shell-20260907`: PASS, two clean native launches, tray/shell ownership,
  close/open, competing-instance activation, graceful exit, and hotkey release.
- `.smoke/p8-final-completion-20260907`: PASS, hidden Work/Break completion, no duplicate
  notification submissions on repeated clock/resume signals, silent Interrupted shutdown.
- Final review: calculations, selected-range isolation, generation checks, native lifecycle wiring,
  phase scope and Git diff reviewed. Whitespace validation passed. Reference/chat hashes unchanged.

## Git delivery

Implementation commit: `b23242060072983a16dcbeece556a17bfbddd2d3`.
Pushed successfully to `origin/native/phased-rewrite`; `git ls-remote` returned that exact SHA
on 2026-09-07. This record is the required documentation-only follow-up commit on the same branch.
Only the preserved, pre-existing untracked `chat_history.txt` remains outside the commits.

## Files changed

- `src/FocusKey.Foundation/Reports/ReportsService.cs`
- `src/FocusKey.Foundation/Reports/ReportsController.cs`
- `src/FocusKey.App/ReportsView.cs`
- `src/FocusKey.App/MainWindow.xaml` and `MainWindow.xaml.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/Startup/StartupContext.cs` and `FoundationBootstrap.cs`
- `tests/FocusKey.Foundation.Tests/Reports/ReportsServiceTests.cs`
- `tests/FocusKey.Foundation.Tests/Reports/ReportsControllerTests.cs`
- `.smoke/ShellProbe/Program.cs` (isolated fixture tooling only)
- `README.md` and root `Phase 8.md`

## Limitations and deferred work

Basic functional layout only; final colors, spacing, typography, animations and responsive polish
remain Phase 12. No custom arbitrary start/end interval or export is specified/implemented; reports
use selected calendar days/weeks/months. No persisted report cache, paging, cloud, telemetry or AI.
Settings and all Phase 9+ work remain deferred. `Focus Key.md`, `Focus Key.zip`, and `chat_history.txt`
are unchanged. No default/legacy database was used for verification.

NO PHASE 9 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 8.
