# Phase 10 — Mini Timer

Workspace: `D:\Focus Key`. Branch: `native/phased-rewrite`.
Baseline: `cc05c9a684c9cdfc0867542f6cd60d2cb9c20157` (Phase 9 complete).

Status: PASS — implementation and verification complete.

## Product behavior and native UX

The Mini Timer is optional and off at startup, following section 8 of `Focus Key.md`.
It opens from the main window's Mini Timer button or the tray's Mini Timer menu item.
The original tray click and Shift+F3 Quick Overlay behavior are preserved. No new settings were added.

One lazily created WinUI utility window is reused for the process lifetime. It shows `Work · mm:ss`
or `Break · mm:ss`, with total minutes supporting sessions longer than an hour. Its client area is
310 × 100 DIPs, with a native draggable caption, X/Alt+F4/Escape hide behavior, a Keep on top toggle,
and Open Focus Key. The toggle and dragged position last for this process only; neither is persisted.
It initially sits near the bottom-right of the monitor work area. Reopening clamps its position to the
nearest monitor work area. It does not occupy a separate taskbar/Alt+Tab entry.

When no session exists, or after a successful Stop/completion, it remains open with `No active session`.
It never starts the next session, displays a second completion notification, or forces itself open.
Hiding the main window does not hide the Mini Timer or stop the session. Explicit application exit
hides the Mini Timer, stops its display timer and disposes the window during resource cleanup.

The ZIP's small draggable type/countdown reference informed the utility layout; its browser countdown
and automatic visibility were not copied. The specification's off-by-default behavior and this phase's
no-additional-settings boundary take precedence. Final visual fidelity remains Phase 12 work.

## Authority, time and lifecycle

`MiniTimerController` is a UI-thread read-only projection over `SessionCoordinator.GetActiveAsync`.
The immutable snapshot is cached only for display. Remaining time is recalculated as
`max(PlannedEndAt - TimeProvider.GetUtcNow(), 0)`; display seconds round upward. The clock is injectable.
The native one-second display callback does not query SQLite, change session status, or persist time.
It stops while hidden, idle, loading, errored or at zero. Brushes update on state/theme/color changes,
not on every display tick.

Opening and existing application start/stop/completion/clock-resume paths request fresh coordinator
observations. Reads run off the UI thread. Completion remains entirely owned by the existing
CompletionCoordinator; a zero countdown awaiting a durable transition is not treated as completed.
The existing startup recovery finishes before a Mini Timer can open, so interrupted/stale sessions
cannot be resurrected by it. Saved durations affect future sessions only; the snapshot keeps each
active session's original deadline. Existing appearance/color events update both visible and reused
windows, using the existing readable black/white foreground calculation on semantic session accents.
System maps to WinUI's default theme and responds to effective theme changes. English display formatting
and explicit reading direction are retained.

Generation checks discard reads superseded by a newer read, hide, or disposal. Read failure clears the
stale snapshot and displays an unavailable/reopen-to-retry message. Hidden refresh requests do no work.
No session engine, persistence schema, polling loop, setting, or recovery policy was introduced.

## Verification — 2026-09-09

- Full build passed with **0 warnings / 0 errors**. Dependencies were unchanged; restore was unnecessary.
- **132 targeted tests passed, 0 failed, 0 skipped**, including 12 new Mini Timer cases. The selected
  regressions cover CompletionCoordinator, SessionCoordinator, QuickOverlay, BackgroundShell,
  appearance/colors and SettingsPageController. The complete suite was not run.
- New deterministic cases cover Work/Break, timestamp accuracy and rounding, delayed observation at
  zero without a write/extra read, custom durations, running-duration immutability, real SQLite Stop
  and completion, original planned-end persistence, startup recovery, hidden/reopened/disposed views,
  stale asynchronous reads and read-failure retry. Shell tests now explicitly verify Mini Timer activation
  forwarding and suppression during shutdown.
- Native Mini Timer smoke PASS: `.smoke/p10-5beba795438246cb8199a2a87420ce68`. It verifies one native
  window, visibility, hide/reopen handle reuse, System/Light/Dark and custom color application, Work
  and Break natural completion while visible, idle state, Quick Overlay interaction, restart off by
  default, persisted color reapplication and graceful cleanup/hotkey release.
- Native shell smoke PASS: `.smoke/p4-f659b51420d444acb5e323cc7c14111f` (two launches, tray,
  close-to-hide/reopen, single-instance behavior and hotkey cleanup).
- Native completion smoke PASS: `.smoke/p6-e3ecb8491bc6437ea01171e14dd4e9ac` (Work/Break notifications,
  repeated signals without duplicates, silent Interrupted shutdown, correct persisted end timestamps).
- The first two new smoke attempts failed on probe argument count/case. The harness was corrected;
  product behavior was not bypassed or weakened. A later probe-enhancement rerun exposed a transient incomplete log read; the harness now waits for a sufficiently long snapshot before checking the same assertion. The successful run above is the final result.
- Direct native UI inspection in `.smoke/p10-ui-20260909` confirmed compact idle layout and the pin
  toggle; Start through the Quick Overlay displayed Work 05:00 with the default accent, then advanced
  from UTC time. Open Focus Key reached Today; Stop immediately showed no active session. Break
  started through the real overlay at 02:00 with custom #FFFF00 and black text in Light appearance.
  Escape hid it; reopening returned the same native handle. Native scripted logs establish state/API
  behavior, while these UI observations establish the described rendered output.
- Final direct native verification confirmed the Keep on top flag changed from false to true, caption dragging moved bounds from 1578,1045,1904,1184 to 1388,1045,1714,1184, Escape hid the window, and the tray command reopened the same handle with the pin and moved position retained. The pending optional user check was completed directly through computer-use and the native probe instead.

## Files changed

- `src/FocusKey.Foundation/MiniTimer/MiniTimerController.cs`
- `src/FocusKey.Foundation/Shell/IShellIntegration.cs`
- `src/FocusKey.App/MiniTimerWindow.cs`
- `src/FocusKey.App/App.xaml.cs`
- `src/FocusKey.App/MainWindow.xaml`
- `src/FocusKey.App/MainWindow.xaml.cs`
- `src/FocusKey.App/Shell/WindowsShellIntegration.cs`
- `tests/FocusKey.Foundation.Tests/MiniTimer/MiniTimerControllerTests.cs`
- `tests/FocusKey.Foundation.Tests/Shell/BackgroundShellTests.cs`
- `.smoke/ShellProbe/Program.cs`
- `.smoke/run-mini-smoke.ps1`
- `Phase 10.md`

## Limits and delivery

Visibility, pin and position are intentionally not saved across restart. There is no Stop/Start control
inside the compact surface; Open Focus Key reaches the existing controls. External database edits
without an application signal are not monitored. A completion persistence failure may leave 00:00
visible until the existing coordinator retries successfully. Broad multi-monitor/DPI certification,
final typography, layout polish and animations remain outside this phase. No unrelated Phase 11
reliability or Phase 12/13 product work was added.

All verification data is isolated under `.smoke` or temporary test directories. Normal user data was
not opened. `Focus Key.md`, `Focus Key.zip`, and the pre-existing untracked `chat_history.txt` retain
their baseline hashes and are excluded from this change.

NO PHASE 11 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 10.

Implementation commit: `fa34934a863d0f7bf1a188fe7197139e52b00a2d`.
Pushed to `origin/native/phased-rewrite`; `git ls-remote` returned that exact SHA on 2026-09-09.
This documentation-only follow-up records the verified delivery. Its own SHA is available in Git history
and the final report, avoiding a self-referential commit hash. No implementation changed after verification.

## Phase 10.1 — Unified Quick Overlay + Active Timer UX

Status: PASS. Baseline: `0da7cb91bb118322e874990c874fde568c0a7553`.

Shift+F3 is now the primary quick surface for both selection and an active session. Start keeps the
same window open and changes it to Work/Break, deadline-derived remaining time and Stop. Opening
while running reconstructs that state. Stop and durable natural completion return to selection
without starting another session. Escape and focus loss hide the surface; repeated activation reuses it.
Native keyboard focus follows the mode, and held Enter cannot immediately stop the session it started.

The standalone Mini Timer remains an optional companion: its pin-able window stays visible across
focus changes, unlike the transient overlay. It is never required for Start, countdown or Stop.
Both surfaces reuse the existing coordinator and Phase 10 RemainingAt/Format calculation. The overlay's
one-second display callback only renders the cached immutable deadline, stops when hidden/idle/at zero,
and never queries SQLite or completes a session. Existing application lifecycle/completion/settings
signals refresh authoritative observations. Appearance and readable Work/Break accents use the existing
settings infrastructure. No schema, settings, engine or recovery policy changed.

Accepted Start/Stop operations survive hiding. Duplicate actions are guarded, stale observations are
discarded, and Stop targets the displayed session ID so it cannot stop a replacement session. A Start
race displays the actual winning session. Failed observations disable stale actions until reopening;
failed mutations retain retry feedback. Hidden operations never force the overlay back open.

### Phase 10.1 verification — 2026-09-09

- 104 targeted tests passed, 0 failed, 0 skipped. Filter: QuickOverlay, MiniTimer,
  CompletionCoordinator, SessionCoordinator, BackgroundShell, Appearance and SessionColors.
  Full suite intentionally not run; session/persistence core behavior is unchanged.
- Deterministic coverage includes Work/Break same-view transitions, custom deadlines, Stop,
  natural completion with PlannedEndAt retained, stale Stop against a replacement, concurrent Start
  winner reconciliation, duplicate Stop, hide/reopen during pending writes, failures and retry.
  Real SQLite is used for relevant lifecycle cases; no real-time waits in unit tests.
- Full solution build: 0 warnings / 0 errors. Restore unnecessary; dependencies unchanged.
- Unified native overlay smoke PASS: `.smoke/p101-f499428cad5b4e10b836127c5becb789`.
  Work/Break custom durations, active hide/reopen and repeated hotkey, identical native window handle,
  natural completion to selection, System/Light/Dark, custom colors, restart and cleanup verified.
- Adjacent native smoke PASS: shell `.smoke/p4-3e89b4614dc444b199df569488af4545`,
  completion `.smoke/p6-af53ea5baf4148aeb22544bafd355f2d`, and retained Mini Timer
  `.smoke/p10-24657374345e4f01a007c7dadc24e090`.
- Manual isolated check in `.smoke/p101-ui-20260909`: user confirmed all requested Start/timer,
  Stop/selection, completion and Escape/Shift+F3 reopening transitions worked. Native diagnostic
  logs additionally show real overlay Start/Stop transitions. Scripted smoke uses isolated probe
  session creation; manual verification establishes the actual interactive Start flow.
- All data roots are isolated. Protected specification/reference/history files remain unchanged.

### Phase 10.1 files changed

- `src/FocusKey.Foundation/Overlay/IQuickOverlayView.cs`
- `src/FocusKey.Foundation/Overlay/QuickOverlayController.cs`
- `src/FocusKey.Foundation/MiniTimer/MiniTimerController.cs`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml.cs`
- `src/FocusKey.App/App.xaml.cs`
- `tests/FocusKey.Foundation.Tests/Overlay/QuickOverlayControllerTests.cs`
- `.smoke/ShellProbe/Program.cs`
- `.smoke/run-overlay-timer-smoke.ps1`
- `Phase 10.md`

### Phase 10.1 limitations and delivery

The functional mode switch retains the existing compact overlay size; animation, final spacing and
visual refinement remain Phase 12 work. A countdown can show zero until the existing completion
coordinator durably completes it. External database edits without an application signal are not polled.
Standalone Mini Timer visibility/pin/position remain process-local as documented for Phase 10.
No Phase 11 work was started. Implementation and remote verification SHAs are recorded below after push.
