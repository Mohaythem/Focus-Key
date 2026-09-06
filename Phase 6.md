# Phase 6 — Notifications & Completion UX

Status: PASS — implementation and final verification complete.
Workspace: `D:\Focus Key`. Branch: `native/phased-rewrite`.

## Baseline and scope

Verified baseline `7f357f5b91ec4bd7f99c9c389cb3d56e1277533f` matched origin before coding.
The full build passed with zero warnings/errors and all 264 tests passed. The existing native
shell smoke passed two launches at `.smoke/p6-baseline-20260906`; Shift+F3, the reference Quick
Overlay, and Escape were also checked on the unchanged build at `.smoke/p6-baseline-overlay`.

`Focus Key.md` section 4 requires a simple sound and Windows notification saying
“Work session completed.”, with no automatic Break, suggestion, or extra popup. Break uses
“Break session completed.” The ZIP contains no separate completion surface to reproduce;
Windows owns notification styling. The original product roadmap differs from the native
phase sequence; the current request and repository roadmap define this phase's scope.
The Active Session Overlay is separate from completion feedback and remains deferred.

## Architecture and timing

`CompletionCoordinator` is an application-lifetime scheduling adapter around the existing
`SessionCoordinator`. Quick Overlay Start now calls this adapter, which persists through the
same Phase 2 engine and arms a one-shot `TimeProvider` timer using `PlannedEndAt - GetUtcNow()`.
It does not maintain another active-session record, decrement counters, or decide terminal state.

A timer wake asks `CompleteIfDueAsync` to evaluate the current persisted session. Delayed wakes
retain the engine's `EndedAt = PlannedEndAt` semantics. Early wakes or backward clock changes
rearm from fresh UTC timestamps. Extremely long waits are capped at one day per timer arm to
stay within native timer limits; production defaults remain Work 30 minutes and Break 10 minutes.
Idle has a disabled timer and no continuous database polling. Database failures and overdue
compare-and-swap conflicts wait ten seconds before retrying, avoiding an immediate retry loop.

The existing native shell window subscribes to suspend/resume notifications and receives
`WM_TIMECHANGE`. Those signals request evaluation; they do not perform recovery or alter the
clock. The registration is released with the shell. Tests control UTC and timer callbacks
explicitly and require no sleeps or real session-duration waits.

## Persistence, concurrency, and delivery

Start, evaluation, notification submission, and shutdown are serialized by a coordinator gate.
The existing repository conditional update remains the authority for exactly one durable
transition, including competing engine/coordinator instances. Only a successful Completed
outcome triggers notification; StillRunning, Conflict, Stopped, and Interrupted do not.

Notifications are submitted once for the transition winner. Repeated evaluations read no
Running session and cannot replay feedback. Notification failures are logged without rolling
back completion or retrying an uncertain submission. There is no notification outbox, delivery
history, or settings persistence. A process failure between database commit and OS submission
can lose feedback; exactly-once visible OS delivery is not claimed. Historical and startup
recovery completions are not replayed as notifications.

Explicit Exit drains in-flight completion work before Phase 3 shutdown and native resource
cleanup. If shutdown itself wins a due completion, it submits feedback once; cached repeated
shutdown results do not repeat it. Future sessions still become Interrupted silently. A failed
shutdown leaves coordination usable and rearms a retry, matching the existing recovery contract.

## Native notification and sound

`WindowsShellIntegration.NotifyCompleted` uses `Shell_NotifyIcon(NIM_MODIFY)` with `NIF_INFO`
and the existing tray identity. Title: `Focus Key`. Body identifies Work or Break. The standard
Windows information-notification sound is enabled, with `NIIF_RESPECT_QUIET_TIME`; there is no
second custom sound, downloaded media, notification activation handler, or forced window focus.
Windows notification/sound preferences can suppress presentation. API success confirms
submission rather than proving a banner was seen or audio was heard.

The notification callback is dispatched to the UI thread and works without a visible main
window or Quick Overlay. An already-visible Quick Overlay refreshes its Running feedback after
completion without opening or focusing a hidden overlay. Stale refreshes cannot repaint a
dismissed/reopened view. Tray menus, single instance, recovery rules, and default durations remain
unchanged.

API references: [notification data and sound flags](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw),
[resume registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registersuspendresumenotification).

## Files changed

- `src/FocusKey.Foundation/Sessions/CompletionCoordinator.cs`
- `src/FocusKey.Foundation/Overlay/QuickOverlayController.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/Shell/WindowsShellIntegration.cs`
- `src/FocusKey.App/Shell/NativeMethods.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/CompletionCoordinatorTests.cs`
- `tests/FocusKey.Foundation.Tests/Overlay/QuickOverlayControllerTests.cs`
- `.smoke/ShellProbe/Program.cs`
- `.smoke/run-completion-smoke.ps1`
- `README.md` and this root `Phase 6.md`

## Verification

The expanded suite covers Work/Break deadlines, delayed/early wakeups, backward UTC changes,
one-shot/idle scheduling, repeated and concurrent evaluation, multiple coordinators over real
SQLite, STOPPED/INTERRUPTED silence, failed notifications without replay, failed reads/writes,
bounded conflict retries, cancelled Start, overdue Start arming, shutdown draining and retries,
due shutdown idempotency, and visible/hidden/stale overlay refresh behavior.

Native Work evidence: `.smoke/p6-completion-native-20260906`, session
`3f931a95-f3e9-4dec-acd7-e76ee47501dc`, planned and actual end both
`2026-09-06T12:27:30.5135087Z`. The user confirmed the correct Work message and one sound.
An additional Work started through the overlay retained the production 30-minute duration;
a competing short Break was correctly rejected. That session was preserved and later ended by
normal explicit Exit.

Native Break evidence: `.smoke/p6-break-native-20260906`, session
`48c7ff68-a7a2-40b0-8caa-abf3cadc9012`, planned and actual end both
`2026-09-06T12:30:41.8669005Z`. The main-window hide was logged before Start; the user confirmed
the correct Break message and one sound while the application remained in the tray.

The short-session probe uses the real engine against an existing, guarded `.smoke` database and
signals the native scheduler. It neither changes production defaults nor edits persisted session
timestamps. The repeatable completion smoke checks both types, exact stored end timestamps,
duplicate signal suppression, silent Interrupted shutdown, and hotkey cleanup. Physical sleep,
hibernation, OS notification suppression, and different sound profiles were not manually tested;
controlled time and native resume-message delivery cover the scheduling path.

Final verification on 2026-09-06:

- `dotnet restore FocusKey.slnx`: passed, all dependencies up to date.
- `dotnet build FocusKey.slnx -c Debug --no-restore`: passed, 0 warnings and 0 errors.
- Complete suite: **286 passed, 0 failed, 0 skipped** (264 baseline plus 22 Phase 6 tests).
- `.smoke/run-completion-smoke.ps1 -DataRootName p6-final-completion-20260906`: passed.
- `.smoke/run-smoke.ps1 -DataRootName p6-final-shell-20260906`: passed, two clean launches,
  tray/main-window behavior, competing-instance rejection, and hotkey cleanup intact.
- Work and Break native messages and one sound each were directly confirmed by the user.
- Final code, test, smoke-script, and diff review completed; `git diff --check` passed.

The default/legacy database, `Focus Key.md`, `Focus Key.zip`, and `chat_history.txt` are unchanged.

## Git delivery

Implementation commit: `7361859d8b42305614d98e7ce301e19f58f061bf`
(`Implement Phase 6 completion coordination and notifications`). Pushed to
`origin/native/phased-rewrite`; `git ls-remote origin refs/heads/native/phased-rewrite`
verified that exact remote SHA on 2026-09-06. This delivery record is a documentation-only
follow-up commit.
Only reviewed source, tests, smoke scripts, and documentation are included. No database, log,
build output, or other local verification artifact is committed.

## Deferred work

No Today, Reports, Settings/settings persistence, Mini Timer, startup registration, packaging,
release, or final visual polish. No Active Session Overlay or new recovery rules.

NO PHASE 7 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 6.
