# Original User Request

## 2026-09-20T19:00:15Z

Focus Key Application UI Scaling / Zoom System: A native, layout-aware, persistent UI scaling system supporting discrete levels (80%, 90%, 100%, 110%, 125%, 150%) with keyboard shortcuts (`Ctrl + Plus`, `Ctrl + Minus`, `Ctrl + 0`), Settings controls, and adaptive layout compensation across the main application window while keeping Quick Overlay excluded.

Working directory: D:\Focus Key
Branch: native/phased-rewrite
Integrity mode: development

---

## Requirements

### R1. Core UI Scaling Architecture & Normalized Factor
- Implement an authoritative application UI scale model supporting discrete percentage levels: `80%`, `90%`, `100%`, `110%`, `125%`, `150%` (normalized scale factors: `0.80`, `0.90`, `1.00`, `1.10`, `1.25`, `1.50`).
- Do NOT use a whole-window visual `ScaleTransform` that causes blurriness or breaks hit-testing; UI elements must be logically remeasured and redrawn at native crispness.
- UI scale adjusts typography sizes, control dimensions, spacing, margins, and layout limits dynamically.
- System DPI awareness and multi-monitor DPI scaling must remain fully intact and compose cleanly with application UI scaling.

### R2. Settings Persistence & Database Migration
- Persist the selected `UiScalePercent` in the SQLite settings database (`application_settings`).
- Create SQLite Migration 14 (`ui_scale_preference`) with a default value of `100%` (`100`) so existing installations experience zero unexpected visual changes.
- Ensure the scale setting survives app restarts, theme changes, and navigation across pages.

### R3. Keyboard Shortcuts & Transient Visual Feedback
- Support standard browser-like keyboard shortcuts on MainWindow:
  - `Ctrl + Plus` / `Ctrl + OemPlus` / `Ctrl + Add`: Step to next scale level (up to 150%).
  - `Ctrl + Minus` / `Ctrl + OemMinus` / `Ctrl + Subtract`: Step to previous scale level (down to 80%).
  - `Ctrl + 0` / `Ctrl + D0` / `Ctrl + NumPad0`: Reset scale immediately to 100%.
- Display subtle, non-intrusive transient feedback (e.g. `UI scale: 125%`) that automatically fades out without modal interruption.
- Ensure shortcuts do not trigger or interfere when typing into text inputs where standard editing takes precedence.

### R4. Settings UI Integration & Live Bidirectional Sync
- Add a `"UI scale"` setting with a ComboBox dropdown (`80%`, `90%`, `100%`, `110%`, `125%`, `150%`) inside `Appearance → Display` in `SettingsView`.
- Changing scale from Settings immediately applies the new scale live without requiring an app restart.
- Changing scale via keyboard shortcuts immediately updates the ComboBox selection if Settings is open.

### R5. Adaptive Layout & Effective Viewport Compensation
- Calculate adaptive layout breakpoints using effective logical width (`effective_width = actual_width / ui_scale_factor`) so that Today, Reports, and Settings transition gracefully to compact/narrow layouts at higher UI scales without clipping, overlapping, or horizontal scrolling.
- Preserve timer typography layout integrity (no digit clipping for values like `00:01`, `09:59`, `22:00`, `59:59`, `100:00`).
- Ensure Reports charts (WebView2 / Chart.js) adjust their logical canvas/font sizing cleanly without transform blur.

### R6. Quick Overlay & Non-Target Surface Exclusion
- Quick Overlay is strictly excluded from UI scaling and must remain at its approved 100% geometry (480 DIP width, 432 DIP progress track, 52 DIP timer) and position logic.
- MainWindow caption controls / system title bar buttons remain standard system-managed.

---

## Acceptance Criteria

### Functionality & Keyboard Navigation
- [ ] `Ctrl + Plus`, `Ctrl + Minus`, and `Ctrl + 0` cycle through discrete scale levels (80%, 90%, 100%, 110%, 125%, 150%) and clamp at boundaries.
- [ ] Transient feedback displays current scale percentage and auto-dismisses.
- [ ] Settings `Appearance → Display` provides a UI scale ComboBox that updates live and synchronizes with keyboard shortcuts.
- [ ] Selected scale survives app restart, page switching, and theme changes.

### Visual & Layout Integrity
- [ ] Text and icons remain crisp at all scale levels (80%, 90%, 100%, 110%, 125%, 150%) without bitmap scaling blur.
- [ ] Today page maintains visual balance without digit clipping or horizontal scrollbars at 80%, 100%, 125%, and 150%.
- [ ] Reports and Settings cards stack and reflow gracefully under Narrow / 150% configurations.
- [ ] Quick Overlay dimensions, typography, and drag behavior remain completely unchanged at 100% scale.

### Automated Tests & Verification
- [ ] Unit tests pass for discrete scale clamping, stepping up/down, resetting to 100%, invalid value fallback, effective width calculation, and SQLite Migration 14 persistence.
- [ ] Full test suite passes (`dotnet test -c Release`) with 0 failures.
- [ ] Release build compiles cleanly (`dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release`) with 0 warnings and 0 errors.
- [ ] Documentation updated in `FUTURE_PLAN.md`, `PROGRESS.md`, and new `Phase 15.md`.
