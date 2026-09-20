# Phase 14 — Settings Information Architecture & Collapsible Sections Redesign

Workspace: `the repository root`. Branch: `native/phased-rewrite`.

Status: Implemented — Awaiting Final User Acceptance.

---

## 1. Executive Summary & Design Architecture

Phase 14 reorganizes and refines the Focus Key Settings surface into a compact, native Windows 11 configuration experience structured by frequency of use. Settings is presented as four prominent top-level Fluent cards (`FkCard`, 1px stroke, 8px corner radius):

1. **SESSION** (Permanently Visible Top-Level Card)
   - Header: `"SESSION"` (SemiBold 13px, padding `16, 14, 16, 10`) + 1px subtle divider
   - Work duration (`TextBox` minutes/seconds)
   - Break duration (`TextBox` minutes/seconds)
   - Session sounds (`ToggleSwitch` on/off)
2. **APPEARANCE** (Collapsible Top-Level Card, Default Collapsed)
   - Header: Full-width clickable 48px card header button with SemiBold 13px title and animated chevron (`\uE76C` collapsed / `\uE70E` expanded)
   - 1px divider + nested sub-cards (`FkCardSubtle`, 6px corner radius):
     - **SYSTEM APPEARANCE**: Color scheme, Contrast
     - **LIGHT THEME**: Preset, Background, Foreground, Accent
     - **DARK THEME**: Preset, Background, Foreground, Accent
     - **SESSION COLORS**: Work color, Break color + Presets
     - **DISPLAY**: Clock format (24h / 12h)
3. **SHORTCUTS** (Collapsible Top-Level Card, Default Collapsed)
   - Header: Full-width clickable 48px card header button + chevron
   - 1px divider + nested sub-cards (`FkCardSubtle`):
     - **QUICK OVERLAY**: Global shortcut button + reset (default `Shift + F3`)
     - **OPEN FOCUS KEY**: Global shortcut button + reset (default `Shift + F4`)
4. **ADVANCED** (Collapsible Top-Level Card, Default Collapsed)
   - Header: Full-width clickable 48px card header button + chevron
   - 1px divider + nested sub-cards (`FkCardSubtle`):
     - **STARTUP**: Start with Windows toggle
     - **QUICK OVERLAY**: Reset position button
     - **DATA**: Import history, Export history

At the bottom of the page, the secondary action bar ("Reload saved values") and status feedback block (`_status`) are anchored with clean separation (`Margin = 4, 20, 4, 16`).

---

## 2. Complete Setting Inventory & Before/After Mapping

Every existing setting is preserved with zero feature loss:

| # | Existing Setting | Prior Section | New Section | Sub-Card | Collapsible? | Preserved Behavior |
|---|---|---|---|---|---|---|
| 1 | Work duration | SESSIONS | **SESSION** | — | No (Permanent) | Real-time validation, non-mutation of active sessions |
| 2 | Break duration | SESSIONS | **SESSION** | — | No (Permanent) | Real-time validation, non-mutation of active sessions |
| 3 | Session sounds | SESSIONS | **SESSION** | — | No (Permanent) | Audio feedback on start and completion |
| 4 | Color scheme | APPEARANCE | **APPEARANCE** | SYSTEM APPEARANCE | Yes | Live WinUI theme switching |
| 5 | Contrast | APPEARANCE | **APPEARANCE** | SYSTEM APPEARANCE | Yes | Higher-contrast border and text styles |
| 6 | Light Theme palette | LIGHT THEME | **APPEARANCE** | LIGHT THEME | Yes | Curated presets + live hex editing |
| 7 | Dark Theme palette | DARK THEME | **APPEARANCE** | DARK THEME | Yes | Curated presets + live hex editing |
| 8 | Clock format | TIME FORMAT | **APPEARANCE** | DISPLAY | Yes | 12/24-hour clock rendering across Today |
| 9 | Session colors | SESSION COLORS | **APPEARANCE** | SESSION COLORS | Yes | Semantic work (teal) & break (violet) palette |
| 10 | Quick Overlay shortcut | SHORTCUTS | **SHORTCUTS** | QUICK OVERLAY | Yes | Global hotkey with conflict rollback |
| 11 | Open Focus Key shortcut | SHORTCUTS | **SHORTCUTS** | OPEN FOCUS KEY | Yes | Global hotkey with conflict rollback |
| 12 | Start with Windows | SYSTEM | **ADVANCED** | STARTUP | Yes | Windows startup registry task integration |
| 13 | Reset Overlay Position | QUICK OVERLAY | **ADVANCED** | QUICK OVERLAY | Yes | SQLite position clear & foreground center |
| 14 | Import history | DATA | **ADVANCED** | DATA | Yes | Tab-delimited CSV historical parser |
| 15 | Export history | DATA | **ADVANCED** | DATA | Yes | Tab-delimited CSV historical export |

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

Each collapsible section uses a dedicated WinUI 3 card header:
- **Visuals**: A full-width subtle button spanning the top of the `FkCard` with section title in semi-bold 13px and a right-aligned Fluent chevron icon (`\uE76C` for collapsed vs. `\uE70E` for expanded).
- **Keyboard Interaction**: Fully focusable via `Tab`; toggled via `Enter` or `Space`.
- **Accessibility / Narrator**: `AutomationProperties.SetName` announces section name and current state (e.g., `"Appearance, expanded"` or `"Appearance, collapsed"`).
- **Tab Focus Hygiene**: When a section is collapsed, its child container has `Visibility = Visibility.Collapsed`, ensuring collapsed controls never accept phantom keyboard focus.

---

## 5. Verification Results

1. **Automated Unit Tests**:
   - `dotnet test -c Release`: 816/816 unit tests passing (0 failed, 0 skipped).
2. **Release Build**:
   - `dotnet build src/FocusKey.App/FocusKey.App.csproj -c Release`: 0 warnings, 0 errors.
3. **Runtime & Visual Verification**:
   - Verified persistence of all 3 expansion flags across app navigation and restarts.
   - Tested responsive behavior across Wide, Medium, and Narrow viewports.
