# Phase 5 — Quick Overlay

Status: PASS — implementation and final verification complete. Branch:
`native/phased-rewrite`.

Phase 5 adds the first user-facing session-start interaction. `Shift + F3` now opens one
reusable native WinUI 3 quick overlay, centered on the monitor containing the foreground window.
The overlay lets the user choose Work (30 minutes) or Break (10 minutes), then start the
selected session. It is a view over the existing Phase 2/3 session rules: the application
controller reads active state and starts through the injected `SessionCoordinator` operations.
No timer, completion scheduler, notification or sound behavior was added.

## Native surface

`QuickOverlayWindow` is a borderless, always-on-top WinUI 3 window with a 420 × 280 DIP reference
surface. It uses the ZIP reference palette: `#111616` overlay surface, `#183739` Work, and
`#434763` Break, with native Segoe UI text and Consolas duration/key text. The shipped ZIP uses
Inter and JetBrains Mono; no fonts were downloaded, so the native fallbacks keep installation
local and dependency-free.

The surface has two native Button cards, a full-width Start button, inline feedback, and keyboard
hints. Left/Right selects Work or Break; Tab moves focus and Space selects the focused card.
Enter starts, Space on Start follows native
button activation, and Escape hides the overlay. Alt+F4 and clicking outside dismiss it through
the native window lifecycle. Native focus visuals remain enabled. The controller disables Start
while a read or write is busy and ignores re-entrant Start requests.

The window is created lazily and reused. Activation moves it to the foreground monitor before
DPI-aware centering. A normal overlay is 420 × 280 DIPs; a feedback state allocates 340 DIPs so
the error text has room to wrap. Dismissal hides the existing window, and successful Start also
hides it. An accepted Start is not cancelled by Escape. Read results carry an observation
generation, so a stale asynchronous read cannot repaint a newly dismissed or reactivated view.

## Controller and session behavior

`QuickOverlayController` owns presentation state only. It obtains the current active session with
`GetActiveAsync` and starts with `StartAsync`; it does not duplicate session validation, timing,
completion, persistence or mutable session authority. An active session leaves the overlay open
with inline feedback and no enabled Start action. Read and start failures remain visible inline and
are reported through the existing application logging path. The no-active-session overlay has no
separate active-session overlay or Stop UI in this phase.

The shell still owns process lifetime and tray behavior from Phase 4. Exit dismisses the overlay,
persists coordinator shutdown, then disposes the overlay on shell exit before closing the main
window. Ordinary close and outside deactivation only hide the overlay.

## Changed files

- `src/FocusKey.Foundation/Overlay/IQuickOverlayView.cs`
- `src/FocusKey.Foundation/Overlay/QuickOverlayController.cs`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml`
- `src/FocusKey.App/Overlay/QuickOverlayWindow.xaml.cs`
- `src/FocusKey.App/App.xaml.cs`
- `tests/FocusKey.Foundation.Tests/Overlay/QuickOverlayControllerTests.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionStore.cs`
- `tests/FocusKey.Foundation.Tests/Sessions/SessionSchemaTests.cs`
- `README.md` and this root `Phase 5.md`

The test helper changes replace global SQLite pool clearing with an owned pool and clear that pool
after each test. This prevents the expanded suite from racing global SQLite teardown. Production
Phase 0–4 session and schema behavior is unchanged.

## Verification

The Phase 0–4 baseline was `871bb8d`; the remote `native/phased-rewrite` reference matched that
baseline before Phase 5 work. Final restore succeeded, the full Debug solution build passed with
0 warnings and 0 errors, and all 264 tests passed (250 baseline tests plus 14 overlay cases),
with 0 failures and 0 skipped tests.

Deterministic controller coverage includes activation/reuse, Work/Break defaults through real
SQLite, existing/racing Running sessions, persistence/read failures, reentrant Start, dismissal,
reopening, stale reads, and disposal. Native keyboard behavior was exercised through Windows
computer-use input; it is not simulated by the headless controller tests.

The final native shell regression smoke passed two clean launches at
`.smoke/p5-verified-20260906-final`. It verified the tray API, main-window close-to-hide and reopen,
competing-instance exit before database initialization, clean shutdown, and hotkey release.

Native observations already recorded include opening the overlay through global `Shift + F3` with
Notepad foreground, stable reuse of the same window handle across hide/reopen, Left/Right Break
selection, Tab/Space selection and Start, a persisted 600-second Break session, and Escape on an
empty database before Start. Evidence for the persisted session is rooted at
`.smoke/p5-final-563405d4c5394ab5a65b9227e6617c9b`.

The final Work/Enter check used `.smoke/p5-work-final-7a54889be63b40559e0e94083b6061ed`:
Enter persisted one Work session from `2026-09-06T06:53:22.8544242Z` to planned end
`2026-09-06T07:23:22.8544242Z`, then hid the overlay. Reopening showed Running feedback with
disabled Start; another Enter left the same single row unchanged. The final 340-DIP feedback
surface was visually verified with both feedback and keyboard hints fully visible. Alt+F4 hid
the overlay without exiting the app, and explicit shell exit completed cleanly. No default or
legacy database was used.

Final review also corrected tests to await each accepted Start directly, rather than reusing an
already-completed hide signal. An expanded-suite run exposed cross-test SQLite pool teardown;
fixture-scoped pool clearing fixed it without weakening assertions or disabling parallel tests.

## Known limitations

The native utility window does not add a desktop-wide dim/blur scrim. DPI-aware foreground-monitor
placement is implemented; native acceptance used the current monitor and normal text scale, so
mixed-DPI monitors and enlarged accessibility text remain unverified. Fonts use the native
fallbacks described above. Observation never completes an overdue session: until a later phase
provides completion coordination, a persisted Running session continues to block Start even
after its planned end. Existing Phase 3 startup/shutdown semantics remain unchanged.

## Git delivery

Implementation commit and verified remote SHA will be recorded after the authorized push.
The design specification, ZIP, and `chat_history.txt` remain byte-for-byte unchanged; no build,
database, or log artifacts are included in the commit.

## Deferred scope

Phase 6 owns notifications, sounds, completion handling and any active-session completion pump.
The active-session overlay, Stop UI, Today, Reports, Settings, Mini Timer, startup registration,
packaging, release work, and later polish remain deferred to their specified phases.

NO PHASE 6 OR LATER PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 5.
