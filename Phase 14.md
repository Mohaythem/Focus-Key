# Phase 14 — Settings Information Architecture & Collapsible Sections Redesign

Workspace: `the repository root`. Branch: `native/phased-rewrite`.

Status: IN PROGRESS.

---

## 1. Executive Summary & Design Architecture

Phase 14 reorganizes the Focus Key Settings surface into a compact, native Windows 11 configuration experience structured by frequency of use. Rather than presenting a long, overwhelming list of 10 flat sections, Settings is restructured into four top-level sections:

1. **SESSION** (Permanently Visible, Top Priority)
   - Work duration (`TextBox` minutes/seconds)
   - Break duration (`TextBox` minutes/seconds)
   - Session sounds (`ToggleSwitch` on/off)
2. **APPEARANCE** (Collapsible Section, Default Collapsed)
   - Color scheme (`ComboBox`: System, Light, Dark)
   - Contrast (`ComboBox`: Standard, Higher Contrast)
   - Light Theme (`ComboBox` Preset + `ColorPicker` Background, Foreground, Accent)
   - Dark Theme (`ComboBox` Preset + `ColorPicker` Background, Foreground, Accent)
   - Clock format (`ComboBox`: 24-hour, 12-hour)
   - Session colors (`ColorPicker` Work color, Break color + Presets)
3. **SHORTCUTS** (Collapsible Section, Default Collapsed)
   - Quick Overlay global shortcut (default `Shift + F3`)
   - Open Focus Key global shortcut (default `Shift + F4`)
4. **ADVANCED** (Collapsible Section, Default Collapsed)
   - Launch with Windows (`ToggleSwitch` on/off)
   - Reset Quick Overlay Position (`Button`: "Reset position")
   - Import history (`Button`: "Import history…")
   - Export history (`Button`: "Export history…")

At the bottom of the page, the secondary action bar ("Reload saved values") and status feedback block (`_status`) remain available for administrative refresh.

---

## 2. Complete Setting Inventory & Before/After Mapping

Every existing setting is preserved with zero feature loss:

| # | Existing Setting | Prior Section | New Section | Collapsible? | Preserved Behavior |
|---|---|---|---|---|---|
| 1 | Work duration | SESSIONS | **SESSION** | No (Permanent) | Real-time validation, non-mutation of active sessions |
| 2 | Break duration | SESSIONS | **SESSION** | No (Permanent) | Real-time validation, non-mutation of active sessions |
| 3 | Session sounds | SESSIONS | **SESSION** | No (Permanent) | Audio feedback on start and completion |
| 4 | Color scheme | APPEARANCE | **APPEARANCE** | Yes | Live WinUI theme switching |
| 5 | Contrast | APPEARANCE | **APPEARANCE** | Yes | Higher-contrast border and text styles |
| 6 | Light Theme palette | LIGHT THEME | **APPEARANCE** | Yes | Curated presets + live hex editing |
| 7 | Dark Theme palette | DARK THEME | **APPEARANCE** | Yes | Curated presets + live hex editing |
| 8 | Clock format | TIME FORMAT | **APPEARANCE** | Yes | 12/24-hour clock rendering across Today |
| 9 | Session colors | SESSION COLORS | **APPEARANCE** | Yes | Semantic work (teal) & break (violet) palette |
| 10 | Quick Overlay shortcut | SHORTCUTS | **SHORTCUTS** | Yes | Global hotkey with conflict rollback |
| 11 | Open Focus Key shortcut | SHORTCUTS | **SHORTCUTS** | Yes | Global hotkey with conflict rollback |
| 12 | Start with Windows | SYSTEM | **ADVANCED** | Yes | Windows startup registry task integration |
| 13 | Reset Overlay Position | QUICK OVERLAY | **ADVANCED** | Yes | SQLite position clear & foreground center |
| 14 | Import history | DATA | **ADVANCED** | Yes | Tab-delimited CSV historical parser |
| 15 | Export history | DATA | **ADVANCED** | Yes | Tab-delimited CSV historical export |

---

## 3. Persistence Architecture

### Database Schema Migration 12
The expansion state of each collapsible section is stored in the authoritative SQLite `application_settings` table:
```sql
ALTER TABLE application_settings ADD COLUMN appearance_expanded INTEGER NOT NULL DEFAULT 0;
ALTER TABLE application_settings ADD COLUMN shortcuts_expanded INTEGER NOT NULL DEFAULT 0;
ALTER TABLE application_settings ADD COLUMN advanced_expanded INTEGER NOT NULL DEFAULT 0;
```

### Domain & Application Models
- `ApplicationSettings`: Added `bool AppearanceExpanded = false`, `bool ShortcutsExpanded = false`, `bool AdvancedExpanded = false`.
- `SqliteSettingsRepository`: Reads and writes the three expansion columns during `LoadAsync` and `SaveAsync`.
- `SettingsService`: Exposes `UpdateAppearanceExpandedAsync`, `UpdateShortcutsExpandedAsync`, `UpdateAdvancedExpandedAsync`.
- `SettingsPageController`: Debounces and queues section toggle saves without blocking the UI thread.

---

## 4. Native Collapsible Header Component & Accessibility

Each collapsible section uses a dedicated WinUI 3 header:
- **Visuals**: A full-width subtle button with section title in semi-bold uppercase (`Style="{StaticResource FkSectionText}"`), and a right-aligned Fluent chevron font icon (`\uE76C` for collapsed `>` vs. `\uE70E` for expanded `v`).
- **Keyboard Interaction**: Fully focusable via `Tab`; toggled via `Enter` or `Space`.
- **Accessibility / Narrator**: `AutomationProperties.SetName` announces section name and current state (e.g., `"Appearance, expanded"` or `"Appearance, collapsed"`).
- **Tab Focus Hygiene**: When a section is collapsed, its child container has `Visibility = Visibility.Collapsed`, ensuring collapsed controls never accept phantom keyboard focus.

---

## 5. Verification Plan

1. **Automated Unit Tests**:
   - `SqliteSettingsRepositoryTests`: Verify migration 12 creates columns, loads defaults (`false`), and preserves saved true/false states.
   - `ApplicationSettingsTests`: Verify validation rules and immutability with expansion flags.
   - Full test suite execution: `dotnet test -c Release` (target: 100% pass rate).
2. **Release Build**:
   - `dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release` (0 warnings, 0 errors).
3. **Runtime & Visual Verification**:
   - Test expansion persistence across navigation and full application restarts.
   - Inspect Wide, Medium, and Narrow responsive viewports in Dark and Light themes.
