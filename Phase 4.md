# Phase 4 — Windows background shell

Status: verified on 2026-09-06. Branch: `native/phased-rewrite`.

Phase 4 adds the Windows background shell around the Phase 0–3 foundation. The application now
owns one instance per user and Windows session, lives in the notification area, accepts the
global `Shift + F3` activation, and keeps the hidden shell alive when the main window is closed.

## Architecture and ownership

`SingleInstanceLease` acquires `Local\FocusKey.Shell.{WindowsUserSid}` before persistence is
initialized. Each launch creates its auto-reset `Local\FocusKey.Shell.{...}.Activation`
event before attempting the mutex, signals the owner to show its window, and exits without
opening or changing the database. Ownership is a UI-thread lease. Windows mutex abandonment
lets a successor acquire ownership after process death; no lock files are used.

`WindowsShellIntegration` creates a hidden popup message window, registers `Shell_NotifyIcon`
with the version 4 callback protocol, and adds two tray actions: Open and Exit. It routes
`NIN_SELECT`/`NIN_KEYSELECT` through the low-word callback action and restores the icon on the
`TaskbarCreated` message. It registers `Shift + F3` with `MOD_SHIFT | MOD_NOREPEAT`; hotkey
activation is routed and logged, with no later product function implemented in this phase.

`BackgroundShell` is the platform-independent lifecycle boundary. It forwards ShowWindow and
Hotkey activation, suppresses activation while exiting, serializes exit requests, and keeps
native resources subscribed when session shutdown fails so Exit can be retried. Resources are
disposed only after shutdown succeeds. The application handles a window close by hiding it;
explicit Exit completes shell shutdown first, then queues the WinUI window close and releases
ownership. The placeholder window's Exit button remains the fallback explicit exit action.

The native adapter keeps its window-procedure delegate rooted, contains exceptions at the
unmanaged boundary, and rolls back partial hotkey/tray/window creation if startup fails.
Startup failure is reported explicitly; shutdown failure leaves the application available
for retry. Successful exit unregisters the hotkey, deletes the tray icon, destroys the native
window/class, and stops listening for activation before releasing ownership. The shared
Windows icon is not destroyed because Windows owns it.

Activation events coalesce during startup. No cross-process acknowledgement or automatic
restart is promised if a launch overlaps an owner's failure or exit; the user can launch
again once that process has exited. Ownership is scoped to the same user and interactive
Windows session, independent of the data-root override. It does not enforce a machine-wide
singleton across different signed-in users or sessions.

The database schema and Phase 0–3 session behavior are unchanged. Phase 4 adds no overlay,
notifications, completion UX, today view, reports, settings, timer, or other Phase 5+ product
functionality.

## Changed files

- `src/FocusKey.Foundation/Shell/BackgroundShell.cs`
- `src/FocusKey.Foundation/Shell/IShellIntegration.cs`
- `src/FocusKey.Foundation/Shell/SingleInstanceLease.cs`
- `src/FocusKey.App/Shell/InstanceActivationSignal.cs`
- `src/FocusKey.App/Shell/NativeMethods.cs`
- `src/FocusKey.App/Shell/WindowsShellIntegration.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/MainWindow.xaml`
- `src/FocusKey.App/MainWindow.xaml.cs`
- `src/FocusKey.App/Startup/FatalError.cs`
- `tests/FocusKey.Foundation.Tests/Shell/BackgroundShellTests.cs`
- `tests/FocusKey.Foundation.Tests/Shell/SingleInstanceLeaseTests.cs`
- `.smoke/ShellProbe/Program.cs`
- `.smoke/ShellProbe/ShellProbe.csproj`
- `.smoke/run-smoke.ps1`
- `README.md` and this root `Phase 4.md`

## Verification

The final verification recorded for this phase was:

- Restore: succeeded; all solution dependencies up to date.
- Build: 0 warnings, 0 errors.
- Full suite: 250 passed, 0 failed, 0 skipped (236 existing tests and 14 shell tests).
- Shell tests cover real SQLite shutdown/retry behavior and dedicated native threads. The
  abandonment test awaits the owner thread handle; `Thread.Join` can return before all kernel
  cleanup is observable, so it retains and disposes the raw mutex after successor acquisition.
- `.smoke/run-smoke.ps1` passed two launches, fresh migrations only on the first launch, actual
  registered tray icon probing, close-to-hide, Open activation, competing launch isolation,
  explicit Exit with exit code 0, shutdown logs, and hotkey release. The final recorded root was
  `.smoke/p4-1b486430fd1f4056956353ae2acf2224`.
- Native Shift+F3 was sent with Notepad foreground and produced the Hotkey log entry. Physical
  tray Open, close-to-hide, and Exit were independently confirmed by the user; clean shutdown logs were
  verified under `.smoke/p4-final-6a847f2acc5047198e2eff191d95f652`.
- Seeded future, due, and observed-time running sessions completed through the shell path and
  were inspected with the probe: future sessions became Interrupted at StartedAt, due sessions
  became Completed at PlannedEnd, and observed sessions became Interrupted at observed time.
- A held hotkey produced the expected Win32 conflict error and a later launch succeeded after it
  was released. The actual native error dialog reported error 1409 and the shortcut conflict;
  evidence is under `.smoke/p4-conflict-dca3ca2865f94dab9ca09cd309abc3c1`.

Seeded native evidence roots:

- Startup interruption: `.smoke/p4-session-future-42d6f09c356f4c74a7a60992835d3b08`.
- Overdue completion: `.smoke/p4-session-due-b870ef789bd64e57a7c03c961701863e`.
- Graceful shutdown with a Running row: `.smoke/p4-session-shutdown-8dd7dd193fff49b1a8266cfa1015b3bf`.

Review repaired version-4 tray callback decoding (event is in LOWORD, not the whole packed
value), added keyboard selection handling, and made protocol setup failure roll back startup.
Test review corrected fixtures that had recovered sessions before the shutdown under test.
Smoke review fixed argument-array binding, second-launch readiness reading old log entries,
and raw SQLite header checks while WAL was open. The header is now checked after graceful
shutdown/checkpoint, while the live schema is checked through bootstrap logging. These were
test/tooling corrections; no session engine or database changes were needed.

The smoke tooling does not claim to prove physical tray clicks or global key delivery; those were
checked separately. Explorer restart/taskbar restoration, shutdown or suspend, full-screen and
elevated-app interaction remain later reliability work. The shared Windows app icon is minimal
and brand refinement is deferred.

Final solution and smoke commands, from `D:\Focus Key`:

```powershell
dotnet restore FocusKey.slnx
dotnet build FocusKey.slnx -c Debug --no-restore
$env:TEMP=Join-Path (Get-Location).Path '.smoke\test-temp'
$env:TMP=$env:TEMP
dotnet test FocusKey.slnx -c Debug --no-build --no-restore
dotnet build .smoke/ShellProbe/ShellProbe.csproj
powershell.exe -NoProfile -File '.smoke\run-smoke.ps1'
git diff --check
git status --short
```

Additional native checks used ShellProbe `hold-hotkey` (release with newline), `seed <root>
future|due`, `exit <pid>`, and `inspect <root>`. The session fixtures were launched with
`FOCUSKEY_DATA_ROOT` set to their respective roots; persisted statuses and exact startup
recovery end timestamps were asserted after exit. The shutdown fixture was seeded after
native shell readiness and asserted to end between its start and planned end.

Astra owned architecture, native correctness fixes, review and final acceptance. Luna provided
bounded adapter scaffolding, ordinary tests, smoke tooling and documentation, all reviewed
before acceptance. No default/legacy database was opened. The specification, design ZIP and
`chat_history.txt` hashes remained unchanged. No test apps or hotkey holders remain running.

## Deferred scope and Git delivery

No Quick Overlay, Work/Break selection overlay, Active Session Overlay, notifications, sounds,
Today, Reports, Settings, Mini Timer, startup registration or packaging was implemented.
TaskbarCreated handling exists but an actual Explorer restart was not forced during verification.

Implementation commit: `d10986c9dda23c3a828c3cd488a884fefe4cb163`
(`Implement Phase 4 Windows background shell`). The push to `origin/native/phased-rewrite`
succeeded. `git ls-remote origin refs/heads/native/phased-rewrite` returned the identical full
SHA, verifying delivery. This delivery record is a documentation-only follow-up commit.
The branch remains `native/phased-rewrite`; no force push, branch rename or history rewrite
was used. Only the unchanged user file `chat_history.txt` remains intentionally untracked.

NO PHASE 5 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 4.
