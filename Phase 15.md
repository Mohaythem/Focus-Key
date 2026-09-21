# Phase 15 — Application UI Scaling / Zoom System

Workspace: `the repository root`. Branch: `native/phased-rewrite`.

Status: Implemented — Verified & Ready for Acceptance.

---

## 1. Executive Summary & Architecture

Phase 15 implements a native, persistent, layout-aware Application UI Scaling / Zoom System in Focus Key. It allows users to scale the main application window across 6 discrete, curated zoom levels to suit high-DPI displays, variable viewing distances, and personal accessibility needs without compromising WinUI 3 crispness or causing blurry bitmap scaling.

### Supported Discrete Levels
- **80%** (Factor 0.80) — Ultra-compact overview
- **90%** (Factor 0.90) — Compact desktop
- **100%** (Factor 1.00) — Default standard
- **110%** (Factor 1.10) — Comfortable reading
- **125%** (Factor 1.25) — High-DPI enlarged
- **150%** (Factor 1.50) — Maximum accessibility

---

## 2. Interaction & Control Surfaces

### 1. Global Keyboard Zoom Shortcuts
- **Zoom In**: `Ctrl + Plus` (`Ctrl + =`, `Ctrl + +`, or Numpad `Ctrl + Add`) steps to the next discrete scale level (clamped at 150%).
- **Zoom Out**: `Ctrl + Minus` (`Ctrl + -` or Numpad `Ctrl + Subtract`) steps to the previous discrete scale level (clamped at 80%).
- **Zoom Reset**: `Ctrl + 0` (`Ctrl + 0` or Numpad `Ctrl + 0`) instantly restores standard 100% scale.
- **TextInput Suppression**: When keyboard focus or event source is inside a `TextBox`, `PasswordBox`, `RichEditBox`, or `AutoSuggestBox`, shortcuts are ignored to prevent typing interference.
- **AltGr Defense**: International keyboard layouts where `AltGr` emits `Ctrl + Alt` are guarded against accidental zoom triggers (`!isAlt && !isWin`).

### 2. Settings Page UI ComboBox
- Located in **Settings $\rightarrow$ APPEARANCE $\rightarrow$ DISPLAY $\rightarrow$ "UI scale"**.
- Options: `80%`, `90%`, `100%`, `110%`, `125%`, `150%`.
- Bidirectional live synchronization: Changing the ComboBox instantly applies the scale to MainWindow and saves to SQLite via `SettingsPageController`. Stepping via keyboard shortcut updates the ComboBox selection live under an `_applying` re-entrancy guard.

### 3. Transient On-Screen HUD Overlay
- When the UI scale changes via keyboard shortcut or ComboBox, a clean Fluent HUD card appears at the bottom-center of the main window (`ScaleHudOverlay`):
  - Displays `"UI scale: 125%"` with icon.
  - Automatically fades out smoothly after 1.5 seconds via a WinUI Storyboard animation (`DoubleAnimation` on Opacity).

---

## 3. Layout-Aware Scaling (No Blurry Root Transform)

Rather than applying a blurry root `ScaleTransform` which degrades text rendering and distorts hit-testing, Focus Key uses layout-aware responsive scaling:

1. **Effective Width Math**:
   $$\text{effectiveWidth} = \frac{\text{physicalWidth}}{\text{scaleFactor}}$$
   Responsive layout breakpoints in `TodayAdaptiveLayoutHelper` and `MainWindow` operate on effective width rather than raw physical window pixels:
   - At high scales (e.g. 150%), the navigation rail transitions gracefully from Expanded to Compact or Collapsed (Hamburger drawer) earlier, preventing content overflow.
   - Today's page layout seamlessly adapts between two-column and single-vertical-stack compositions based on effective available space.
2. **Proportional Dimension & Typography Scaling**:
   - Navigation rail width: Expanded `Math.Round(220 * factor)`, Compact `Math.Round(54 * factor)`, Drawer `Math.Round(240 * factor)`.
   - Timer typography: `RunningText` and `IdleDurationText` font sizes scale with factor (`Math.Round(72 * factor)` on wide layouts, responsive clamped sizes on narrow layouts) ensuring unclipped digit rendering for all session types.
   - Metric numerals: Focus Time, Work Sessions, Break Time, and Completion Rate font sizes scale cleanly (`Math.Round(24 * factor)`).
   - Card paddings and min-heights: `SessionHeroCard`, `TodaySummaryCard`, and `ActivityCard` scale paddings and minimum dimensions proportionally.
   - Action buttons: Button heights (`Math.Round(36 * factor)`), minimum widths, and font sizes scale consistently.

---

## 4. Quick Overlay Strict Isolation

The Quick Overlay window (`QuickOverlayWindow`) remains strictly locked at 100% scale (fixed 480 DIP width), completely isolated from `MainWindow` zoom adjustments. This guarantees that the minimal quick overlay HUD remains compact, predictable, and unobtrusive on the user's screen.

---

## 5. Persistence & Concurrency Architecture

### SQLite Schema Migration 14
```sql
ALTER TABLE application_settings ADD COLUMN ui_scale_percent INTEGER NOT NULL DEFAULT 100;
```

### Authoritative Single-Queue Synchronization
All UI scale persistence requests (whether originating from the Settings ComboBox or rapid keyboard shortcuts) route through `SettingsPageController.UpdateUiScaleAsync`. This guarantees:
- Complete ordering and serialization through `_controller.Enqueue()`.
- Version incrementing on each scale change so that older in-flight saves never clobber newer shortcut or ComboBox selections.
- Zero SQLite `SQLITE_BUSY` database lock contention or thread deadlocks during high-frequency bursts (e.g. 50 keypresses in 100ms).

---

## 6. Verification & Test Evidence

1. **Unit & Empirical Stress Test Suite**:
   - Command: `dotnet test -c Release`
   - Result: **1,136 passed, 0 failed, 0 skipped**.
   - Verified discrete string/int bijective mappings, integer boundary clamping, AltGr modifier matrix defense, text input hierarchy suppression, rapid alternating synchronization, high-frequency stepping bursts, and SQLite cold reload recovery.
2. **Release Build**:
   - Command: `dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release`
   - Result: **0 Warning(s), 0 Error(s)**.
