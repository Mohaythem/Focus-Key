# Project: Focus Key UI Scaling / Zoom System

## Architecture
Focus Key is built on **.NET 10** and **WinUI 3 (Windows App SDK 2.4.0)**. The solution consists of:
- `src/FocusKey.Foundation`: Pure domain logic, settings models, SQLite persistence, schema migrations, and adaptive layout helpers.
- `src/FocusKey.App`: Native WinUI 3 desktop application, windows (`MainWindow`, `QuickOverlayWindow`), views (`TodayPanel`, `ReportsView`, `ReportsChart`, `SettingsView`), and appearance coordinators.
- `tests/FocusKey.Foundation.Tests`: xUnit test suite for domain validation, persistence, schema migrations, and layout algorithms.

### Scaling Architecture Philosophy
- **Crisp, Layout-Aware Scaling**: Window-level visual `ScaleTransform` is prohibited because post-layout GPU texture scaling blurs text and borders. In WinUI 3, native crispness is preserved by scaling the inner content container (`PageScrollViewer` / content panel) with `UseLayoutRounding = true`, allowing DirectWrite to rasterize glyphs at native physical pixel boundaries.
- **Effective Viewport Compensation**: Viewport breakpoints are calculated using:
  $$\text{effective\_width} = \frac{\text{actual\_width}}{\text{ui\_scale\_factor}}$$
  This triggers responsive narrow/compact layouts (e.g. collapsing sidebar to hamburger drawer, stacking multi-column cards) at higher UI scales, preventing horizontal clipping or overflow.
- **Strict QuickOverlay Isolation**: `QuickOverlayWindow` is an independent top-level Win32 `Window` with its own `HWND` and detached `XamlRoot`. It does not inherit `MainWindow`'s visual tree and remains fixed at 100% scale (480 DIP width, 432 DIP progress track, 52 DIP timer).
- **System Caption Controls**: `MainWindow` title bar controls are managed by Windows DWM in the Win32 non-client area and are immune to XAML scale transformations.

---

## Feature Inventory
Every feature identified during the Survey phase is assigned to a milestone below:

| # | Feature | Description | Milestone | Status | Source |
|---|---------|-------------|-----------|--------|--------|
| 1 | Discrete Scale Model & Domain Helper | `UiScaleLevels` utility (80, 90, 100, 110, 125, 150; factors 0.80..1.50; step up/down, clamping, effective width) | M1 | DONE | ORIGINAL_REQUEST §R1 |
| 2 | SQLite Migration 14 (`ui_scale_preference`) | Add `ui_scale_percent INTEGER NOT NULL DEFAULT 100` to `application_settings` table in SQLite | M1 | DONE | ORIGINAL_REQUEST §R2 |
| 3 | Domain Validation & Repository Persistence | `ApplicationSettings.UiScalePercent`, domain validation in `Validate()`, `SqliteSettingsRepository` read/write, `SettingsService.UpdateUiScalePercentAsync` | M1 | DONE | ORIGINAL_REQUEST §R1, §R2 |
| 4 | MainWindow Keyboard Shortcuts | `Ctrl+Plus`, `Ctrl+Minus`, `Ctrl+0` (including keypad `VK_ADD`, `VK_SUBTRACT`, `VK_NUMPAD0`) across all pages | M2 | DONE | ORIGINAL_REQUEST §R3 |
| 5 | Text Input Typing Suppression | Suppress zoom shortcuts when user is focused inside `TextBox`, `PasswordBox`, `RichEditBox`, or `AutoSuggestBox` | M2 | DONE | ORIGINAL_REQUEST §R3 |
| 6 | Transient Visual HUD Overlay | Auto-dismissing bottom-center pill (`UI scale: X%`), non-interactive (`IsHitTestVisible="False"`), smooth fade-out | M2 | DONE | ORIGINAL_REQUEST §R3 |
| 7 | Settings UI ComboBox | `UI scale` ComboBox in `Appearance → Display` in `SettingsView.cs` with auto-save via `SettingsPageController` | M3 | DONE | ORIGINAL_REQUEST §R4 |
| 8 | Live Bidirectional Sync | Instant live sync between keyboard shortcuts and Settings ComboBox without duplicate persistence | M3 | DONE | ORIGINAL_REQUEST §R4 |
| 9 | Effective Viewport Compensation | `effective_width = actual_width / ui_scale_factor` in `TodayAdaptiveLayoutHelper`, `MainWindow`, and `ReportsView` | M4 | DONE | ORIGINAL_REQUEST §R5 |
| 10 | Timer Typography Integrity | Monospaced `Consolas` timer text in TodayView never clips for `00:01` through `100:00` across all scale levels | M4 | DONE | ORIGINAL_REQUEST §R5 |
| 11 | Reports Chart Native Scaling | Dynamic recalculation of native XAML bar widths, grid lines, and labels in `ReportsChart.cs` under scaled viewports | M4 | DONE | ORIGINAL_REQUEST §R5 |
| 12 | Quick Overlay Strict 100% Exclusion | Verified 100% geometry (480 DIP width, 432 DIP progress track, 52 DIP timer) and zero scale contamination | M5 | DONE | ORIGINAL_REQUEST §R6 |
| 13 | MainWindow Caption Controls Standard | Verify standard OS DWM caption controls remain unaffected by XAML layout scaling | M5 | DONE | ORIGINAL_REQUEST §R6 |
| 14 | Final Verification, Build & Documentation | 100% test pass (`dotnet test -c Release`), 0 warnings Release build, documentation in `FUTURE_PLAN.md`, `PROGRESS.md`, `Phase 15.md` | M6 | DONE | ORIGINAL_REQUEST §Acceptance |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|--------------|--------|
| **M0** | E2E Test Suite Track | Independent test harness and comprehensive test cases (Tiers 1-4) | none | DONE |
| **M1** | Core Scale Model & Persistence | `UiScaleLevels.cs`, `ApplicationSettings.cs`, `SchemaMigration.cs` (Migration 14), `SqliteSettingsRepository.cs`, `SettingsService.cs`, and unit tests | none | DONE |
| **M2** | Keyboard Shortcuts & Transient HUD | `MainWindow.xaml` HUD overlay, `MainWindow.xaml.cs` `OnMainSurfacePreviewKeyDown` shortcuts, `IsTextInput` guard, auto-fadeout timer | M1 | DONE |
| **M3** | Settings UI & Bidirectional Sync | `SettingsView.cs` Appearance -> Display ComboBox, `SettingsPageController.cs`, `ApplyUiScale` live bidirectional sync | M1, M2 | DONE |
| **M4** | Adaptive Layout & Viewport Compensation | `TodayAdaptiveLayoutHelper.cs`, `MainWindow.xaml.cs`, `ReportsView.cs`, timer typography integrity, and native reports chart scaling | M1, M2 | DONE |
| **M5** | Surface Exclusion Verification | Quick Overlay 100% isolation verification (480 DIP width, 432 DIP track, 52 DIP timer) and standard caption buttons verification | M1, M4 | DONE |
| **M6** | Final Verification, Hardening & Docs | 100% E2E test suite pass, Tier 5 adversarial testing, Release build validation, documentation updates | M0, M1, M2, M3, M4, M5 | DONE |

---

## Interface Contracts

### `UiScaleLevels` (in `FocusKey.Foundation.Settings`)
```csharp
namespace FocusKey.Foundation.Settings;

public static class UiScaleLevels
{
    public const int DefaultPercent = 100;
    public const int MinPercent = 80;
    public const int MaxPercent = 150;

    public static readonly int[] All = [80, 90, 100, 110, 125, 150];

    public static bool IsValid(int percent);
    public static double ToFactor(int percent);
    public static int NextLevel(int currentPercent);
    public static int PreviousLevel(int currentPercent);
    public static double CalculateEffectiveWidth(double actualWidth, double factor);
}
```

### `ISettingsService` & `SettingsService`
```csharp
public Task<ApplicationSettings> UpdateUiScalePercentAsync(int value, CancellationToken cancellationToken = default);
```

### `SettingsPageController`
```csharp
// SettingsField enum includes UiScale
public Task UpdateUiScaleAsync(int percent, CancellationToken cancellationToken = default);
```

### `MainWindow` ↔ `SettingsView` Live Sync
```csharp
// MainWindow invokes SettingsView to update selection without firing duplicate saves:
internal void ApplyUiScale(int percent);
```

### `TodayAdaptiveLayoutHelper`
```csharp
public static double CalculateEffectiveWidth(double actualWidth, double uiScaleFactor);
public static AdaptiveNavMode ResolveNavMode(double windowWidth, double uiScaleFactor = 1.0);
public static TodayCompositionMode ResolveTodayComposition(double availableContentWidth, double uiScaleFactor = 1.0);
```

---

## Code Layout

- `src/FocusKey.Foundation/Settings/UiScaleLevels.cs`: Domain helper for scale levels, factor conversion, stepping, and effective width. [DONE]
- `src/FocusKey.Foundation/Settings/ApplicationSettings.cs`: Domain record adding `UiScalePercent` with validation in `Validate()`. [DONE]
- `src/FocusKey.Foundation/Data/SchemaMigration.cs`: Appending Migration 14 (`ui_scale_preference`). [DONE]
- `src/FocusKey.Foundation/Settings/SqliteSettingsRepository.cs`: Persistence mapping in `LoadAsync` and `SaveAsync`. [DONE]
- `src/FocusKey.Foundation/Settings/SettingsService.cs`: Exposing `UpdateUiScalePercentAsync`. [DONE]
- `src/FocusKey.Foundation/Settings/SettingsPageController.cs`: `SettingsField.UiScale` and `UpdateUiScaleAsync`. [DONE]
- `src/FocusKey.Foundation/Today/TodayAdaptiveLayoutHelper.cs`: Adding `CalculateEffectiveWidth` and `uiScaleFactor` parameter overloads.
- `src/FocusKey.App/MainWindow.xaml`: Adding `ScaleHudOverlay` Border at `MainSurface` root with `Canvas.ZIndex="2000"`.
- `src/FocusKey.App/MainWindow.xaml.cs`: Shortcut handling in `OnMainSurfacePreviewKeyDown`, `IsTextInput` guard, `ApplyUiScale` content scaling, and HUD animation.
- `src/FocusKey.App/SettingsView.cs`: `_uiScale` ComboBox in `Appearance -> Display` Sub-card E, and `ApplyUiScale` bidirectional sync.
- `src/FocusKey.App/ReportsView.cs`: Effective width layout reflow compensation.
- `tests/FocusKey.Foundation.Tests/Settings/`: Unit tests for Migration 14, persistence, domain validation, and controller auto-save. [DONE]
- `tests/FocusKey.Foundation.Tests/Today/`: Unit tests for `TodayAdaptiveLayoutHelper` effective width calculations.
