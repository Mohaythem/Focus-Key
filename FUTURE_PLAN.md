# Focus Key — Future Implementation Plan

This document serves as the persistent repository-level source of truth for all approved future Focus Key work that has not yet been implemented.

---

## Planning Rules for All Future Work

1. **Immediate Synchronization**: Whenever the user approves, changes, removes, postpones, or adds any future Focus Key idea, update `FUTURE_PLAN.md` immediately so it continuously reflects the latest agreed plan.
2. **Completion Tracking**: Whenever a planned item is implemented and accepted, mark it completed or move it to the [Completed Work](#completed-work) section instead of leaving it as pending.
3. **Scope Fidelity**: Do not invent requirements or assume unapproved scope. Only document agreed-upon architectural, functional, and design decisions.

---

## Active Work (Current Roadmap Task)

*None currently active.*

---

## Implemented Work Awaiting Final User Acceptance

### Application UI Scaling / Zoom System (Implemented — Awaiting Final User Acceptance)
**Status:** Implemented — Awaiting Final User Acceptance
**Scope & Deliverables:**
1. **Discrete Scale Levels**: 6 supported levels (`80%`, `90%`, `100%`, `110%`, `125%`, `150%`).
2. **Keyboard Shortcuts**: `Ctrl + Plus` (Zoom In), `Ctrl + Minus` (Zoom Out), `Ctrl + 0` (Reset to 100%) with TextInput suppression and AltGr defense.
3. **Settings UI ComboBox**: Added under `APPEARANCE -> DISPLAY -> "UI scale"` with full bidirectional live sync and re-entrancy protection.
4. **Transient HUD Overlay**: Auto-fadeout `ScaleHudOverlay` displaying `"UI scale: X%"` for 1.5 seconds upon scale changes.
5. **Layout-Aware Visual Scaling (No Blurry Transform)**: Effective width math (`effectiveWidth = physicalWidth / factor`) driving responsive navigation and Today composition; proportional scaling of typography, timer digits, card paddings, button heights, and nav rail dimensions.
6. **Quick Overlay Isolation**: Quick Overlay locked strictly at 100% (480 DIP width) and untouched.
7. **Authoritative Persistence & Concurrency**: SQLite Migration 14 (`ui_scale_percent`) with single-queue serialization via `SettingsPageController` eliminating race conditions.

---

## Implemented Work Awaiting Final User Acceptance

### Settings Final Polish & Session Sounds Refinement (Implemented — Awaiting Final User Acceptance)
**Status:** Implemented — Awaiting Final User Acceptance
**Scope & Deliverables:**
1. **Settings Page Title & Hierarchy**: Add real "Settings" page title consistent with Today and Reports typography, move SESSION card lower with comfortable spacing (16 DIP).
2. **Sub-Card Dividers & Border Definition**: Add subtle theme-aware dividers between sub-card titles and content; strengthen card and sub-card boundary strokes for clean definition in Dark (`#363636`) and Light (`#E5E5E5`) modes.
3. **Right-Edge Alignment Normalization**: Audit and align trailing controls (durations, master and individual sound toggles, preview buttons, comboboxes, shortcut buttons, Start with Windows toggle, reset position, import/export buttons).
4. **Settings Copy Audit**: Audit every visible label and description for sentence case, consistent product naming, clarity, and conciseness.
5. **Session Sounds Architecture**:
   - Master gate toggle (`Session sounds [On/Off]`) controlling playback permission without overwriting child preferences.
   - Individual `Start sound` setting with description, `Preview` button, and On/Off toggle switch.
   - Individual `Completion sound` setting with description, `Preview` button, and On/Off toggle switch.
   - Sound audition / Preview playing sound immediately even if master or individual sound is disabled.
   - Visually subdued child controls (`Opacity = 0.45`, `IsEnabled = false`) when master toggle is OFF.
6. **Playback Semantics & Backward Compatibility**:
   - Start sound plays on new Work/Break start (not on resume/continue, not on selection changes).
   - Completion sound plays on natural completion (not on stop/pause/interrupt).
   - SQLite migration 13 for `start_sound_enabled` and `completion_sound_enabled` with backward-compatible defaults.

---

## Implemented Work Awaiting Final User Acceptance

### Settings Visual Hierarchy & Nested Card Refinement (Implemented — Awaiting Final User Acceptance)
**Status:** Implemented — Awaiting Final User Acceptance
**Scope & Deliverables:**
1. **Top-Level Sections as Prominent Native Fluent Cards**:
   - `SESSION` (Permanent): Outer `FkCard` container (8px corner radius, 1px stroke), `"SESSION"` SemiBold 13px title header, 1px divider, and 3 session duration/sound rows.
   - `APPEARANCE` (Collapsible): Outer `FkCard` container, full-width 48px header button with SemiBold 13px title and animated chevron (`\uE76C` collapsed / `\uE70E` expanded), 1px divider, and nested sub-cards.
   - `SHORTCUTS` (Collapsible): Outer `FkCard` container, full-width 48px header button + chevron, 1px divider, and nested sub-cards.
   - `ADVANCED` (Collapsible): Outer `FkCard` container, full-width 48px header button + chevron, 1px divider, and nested sub-cards.
2. **Nested Sub-Cards**:
   - Expanded content grouped into clean sub-cards (`FkCardSubtle` surface, 6px corner radius, subtle uppercase title headings with `FkSectionText` style and 60 character spacing).
   - `APPEARANCE`: `SYSTEM APPEARANCE`, `LIGHT THEME`, `DARK THEME`, `SESSION COLORS`, `DISPLAY`.
   - `SHORTCUTS`: `QUICK OVERLAY`, `OPEN FOCUS KEY`.
   - `ADVANCED`: `STARTUP`, `QUICK OVERLAY`, `DATA`.
3. **Relocated Administrative Footer**:
   - Auto-save feedback note (`_status`) and reload button (`_reload`) placed at the true bottom of the Settings page with clear vertical margin separation from the cards (`Margin = 4, 20, 4, 16`).
4. **Persistent SQLite Section Expansion (Migration 12)**:
   - Persist `AppearanceExpanded`, `ShortcutsExpanded`, `AdvancedExpanded` preferences via SQLite `application_settings`.
   - Default state on first launch: Session always visible, Appearance collapsed (`false`), Shortcuts collapsed (`false`), Advanced collapsed (`false`).
   - Persists across page navigation and application restarts.
5. **Preserve 100% of Existing Settings & Logic**:
   - Zero dropped features (all 15 settings preserved), zero regressed behaviors (transactional shortcuts, live theme switching, SQLite duration validation).

### Post-Today UX Refinement Pass (Implemented — Awaiting Final User Acceptance)
Implemented a focused UX refinement pass after the accepted Today adaptive redesign:
1. **Keyboard Navigation & Activation**:
   - **Idle Work / Break Selector**: Left Arrow selects Work, Right Arrow selects Break, Enter/Space activates. Distinguishable selected vs focused states with clean WinUI focus borders.
   - **Session Actions**: Tab navigation reaches Work/Break selector and Start in Idle; Pause in Running; Continue and Start New in Paused. Enter/Space activates the focused action. Hidden/collapsed controls are never keyboard focusable.
   - **App Navigation**: Up / Down moves focus between navigation items in Expanded pane, Compact rail, and Narrow drawer. Enter/Space opens destination. Esc closes narrow drawer without hiding main window or quitting app, and focus returns to Hamburger button.
   - **Focus Visuals**: Clean, visible native WinUI/Fluent focus visuals in Dark and Light themes.
2. **Preserve Session-Colored Today Dot**:
   - Work active/paused session: teal Today status dot.
   - Break active/paused session: violet Today status dot.
   - Idle: no dot. No extra text in navigation chrome.
   - Preserved across Expanded, Compact rail, Narrow/collapsed, and Drawer.
3. **Today's Activity Overflow Scrolling**:
   - On Wide and Medium layouts, Activity surface retains stable page geometry and uses internal `ScrollViewer` (capped at 420 DIP) when row count exceeds container height (Hero and Summary remain stable, page does not stretch infinitely).
   - On Narrow layouts, content reflows into single vertical stack with natural page scrolling.
4. **Reports Month Four Weekly Buckets**:
   - Every month uses EXACTLY 4 weekly buckets in domain aggregation, chart data, labels, tooltips, and summaries:
     - Week 1: Days 1–7 (`YYYY-MM-01 – YYYY-MM-07`)
     - Week 2: Days 8–14 (`YYYY-MM-08 – YYYY-MM-14`)
     - Week 3: Days 15–21 (`YYYY-MM-15 – YYYY-MM-21`)
     - Week 4: Days 22 through end of month (28, 29, 30, or 31).
   - Never produces a Week 5.
   - Comprehensive tests for 28-day Feb, 29-day Feb, 30-day month, 31-day month, and month boundaries.
5. **Quick Overlay Drag Usability**:
   - Draggable region covers the entire upper header background, empty chrome space, and non-interactive header text.
   - Interactive controls (Close button, Work/Break selector, Start, Pause, Continue, Start New) remain responsive and do not trigger window dragging.
   - Preserves native Windows dragging (`WM_NCLBUTTONDOWN`), position persistence, multi-monitor clamping, and Settings reset.

---

#### Keyboard-First + Quick Overlay Drag Correction (Implemented — Awaiting Final User Acceptance)
Implemented focused keyboard-first UX, directional arrow navigation, continuous pointer-capture Quick Overlay dragging, and Activity scrollbar breathing room:
1. **Today Keyboard-First Experience (No Tab Required)**:
   - **Idle State**: When Today is active, keyboard navigation immediately targets the session selection context without requiring Tab first. Left Arrow selects Work, Right Arrow selects Break, Enter/Space starts the session.
   - **Running State**: Enter or Space immediately pauses the session without requiring Tab first.
   - **Paused State**: Continue is focused by default; Left/Right moves between Continue and Start New; Enter/Space activates the focused button.
   - Tab navigation remains available as a standard alternative without focus trapping.
2. **Deterministic Navigation Arrow Keys**:
   - Up/Down arrows move focus cleanly through navigation destinations in Expanded pane, Compact rail, and Narrow drawer (`OnNavGridPreviewKeyDown`, `OnNavDrawerPanePreviewKeyDown`). Enter/Space activates destination; Esc closes the drawer and returns focus to Hamburger button.
3. **Quick Overlay Drag Correction**:
   - Replaced modal `WM_NCLBUTTONDOWN` non-client loop with standard WinUI 3 pointer capture (`CapturePointer`, `GetCursorPos`, `AppWindow.Move`).
   - True press-drag-release behavior: Left button down $\rightarrow$ hold and move mouse $\rightarrow$ window follows continuously $\rightarrow$ release mouse button $\rightarrow$ drag terminates immediately with zero toggle stickiness.
   - Interactive controls (Close `X`, Work, Break, Start, Pause, Continue, Start New) do NOT trigger window dragging.
   - Preserves SQLite position persistence and multi-monitor bounds clamping.
4. **Activity Overflow Scrollbar Padding**:
   - Added safe right padding (`Padding="0,0,10,0"`) and row column spacing with `CharacterEllipsis` so "Completed" and all status text are never clipped or hidden beneath the vertical scrollbar.

---

### Today Native Windows Adaptive Redesign (Implemented — Awaiting Final User Acceptance)
Redesigned and implemented the Today page as a true adaptive native Windows 11 desktop experience inspired by the Windows Clock app adaptive behavior:
1. **Desktop-First Composition**: Today uses a desktop-first Hero + Summary + Activity composition.
2. **Wide Layout (>= 1060 DIP)**:
   - Expanded navigation pane (220 DIP) with icons + text labels.
   - Hero on the left column (~62%).
   - Summary below Hero on the left column.
   - Today's Activity as a dedicated right-side vertical surface (~38%).
3. **Medium Layout (740 to 1059 DIP)**:
   - Compact navigation rail (54 DIP) with centered icons only.
   - Preserves the two-column Today composition (Hero + Summary on left, Activity on right).
4. **Narrow Layout (< 740 DIP)**:
   - Navigation pane collapsed (0 DIP) behind top-left hamburger button (`\uE700`).
   - Clicking hamburger opens a sliding drawer navigation pane over a backdrop.
   - Content adapts to a single vertical stack: Hero first → Daily Summary below Hero → Today's Activity below Summary.
5. **Work / Break Selection**:
   - Integrated compact Work / Break selector exists ONLY when Idle (`[ • Work · X min ]  [ • Break · Y min ]`).
6. **Session Identity**:
   - Running and Paused show session identity (`• WORK SESSION` / `• BREAK SESSION` with `RUNNING` or `PAUSED` badge), not a selectable Work/Break control.
7. **No Circular Ring**:
   - Eliminated giant circular progress timer ring.
8. **Timer & Progress Language**:
   - Large bold countdown timer + 6 DIP thin linear progress bar.
   - Zero-layout-jump across Idle, Running, and Paused states.
9. **Daily Summary Surface**:
   - Daily Summary is ONE coherent 2×2 surface (Focus Time, Work Sessions, Break Time, Completion Rate with subtle horizontal divider).
10. **Activity Placement & Scrolling**:
    - Activity is a dedicated right-side surface on wide/medium layouts and stacks below on narrow layouts, using internal native `ScrollViewer` when session rows exceed container height.
11. **Active-Session Navigation Indicator**:
    - Shows a restrained status dot on the Today navigation item (expanded label dot, compact rail icon dot, hamburger button dot, and drawer dot) when a session is Running or Paused.
    - Uses active session color (teal for work, purple for break).
    - No extra text such as "Running" in navigation chrome.
    - Hidden when there is no active session.
12. **Timer Digit Glyph Integrity**:
    - Large countdown digits render completely without left/right edge clipping (`CharacterSpacing="0"`, container horizontal padding, `TextWrapping="NoWrap"`, `TextTrimming="None"`, responsive font sizing 72pt Wide/Medium, 56pt Narrow).


### Quick Overlay Surgical Visual Restore (Implemented — Awaiting Final User Acceptance)
Surgically restored Quick Overlay visual presentation, proportions, geometry, and layout to the approved `a19b522` baseline:
1. **Visual Presentation & Proportions**:
   - Surface width restored to `480` DIP with padding `24,20,24,20` (height `220` DIP active, `280` DIP idle).
   - Left-aligned `52` DIP bold Consolas countdown timer (`ActiveRemaining`).
   - `432` DIP width × `6` DIP height progress bar track (`ProgressTrack`) with rounded caps and active session color.
   - Persistent compact header (`FOCUS KEY` in idle, `• WORK SESSION` / `• BREAK SESSION` in active, `• WORK SESSION (PAUSED)` in paused).
   - Selection cards in idle restored to `FkOverlayCard` with left content alignment and clean margins.
   - Action buttons right-aligned at bottom in active/paused states.
2. **Retained Approved Interactions**:
   - **Running state**: `[ Pause ]` only (no visible Stop button).
   - **Paused state**: `[ Start New ]` (neutral secondary) and `[ Continue ]` (accent primary) side-by-side (no visible Stop button).
   - **Idle state**: Work / Break selection cards + full-width `[ Start ]` button.
   - Underlying Stop/session-finalization architecture strictly preserved.
3. **Preserved Shell & System Features**:
   - Native header dragging via Win32 `WM_NCLBUTTONDOWN`, SQLite position persistence (`overlay_position_x`, `overlay_position_y`), multi-monitor work-area clamping, default center on foreground display, and Reset position in Settings.
   - Global shortcuts (`Shift + F3` Overlay, `Shift + F4` Main Window) and single-instance activation.
   - Today visual design, Window Close Experience (`0a1f457`), Settings, and 12/24-hour preferences untouched.


### Window Close Experience: Hide or Quit (Implemented — Awaiting Final User Acceptance)
Implemented native Windows 11 close decision modal when closing the main window:
1. **Trigger & Presentation**:
   - Intercept main-window native title bar Close (`X`) button and `WM_CLOSE`.
   - Present a native WinUI modal decision surface (`ContentDialog`) over `MainWindow`.
   - Title: `Close Focus Key?`
   - Body: `Hide Focus Key to keep it running in the system tray, or quit the app completely.`
   - Primary action: `[ Hide Focus Key ]` (accent elevated)
   - Secondary action: `[ Quit Focus Key ]` (neutral/distinct)
   - `Esc` or `Cancel` button: Dismisses dialog and leaves `MainWindow` open.
2. **Distinct Window Behaviors**:
   - **Minimize**: Direct hide-to-tray with no dialog (existing behavior strictly preserved).
   - **Close (X)**: Presents the Hide or Quit decision dialog.
   - **Tray Exit**: Performs canonical graceful shutdown directly with no dialog.
   - **Quick Overlay Close/Esc**: Dismisses/hides overlay only without affecting main window.
   - **Focus Loss**: Normal OS focus behavior (does not hide).
3. **Hide vs. Quit Semantics**:
   - **Hide Focus Key**: Hides `MainWindow` from desktop and taskbar, keeps process and tray icon alive, preserves running/paused sessions, global shortcuts (`Shift + F3`, `Shift + F4`), and Quick Overlay. Restores seamlessly via tray or `Shift + F4`.
   - **Quit Focus Key**: Reuses canonical tray-exit application shutdown path, tearing down hotkeys, tray icon, timers, and persisting active/paused sessions under established recovery rules.
4. **Safety & Reentrancy**:
   - Single dialog guard preventing duplicate dialog instances on rapid clicks or duplicate `WM_CLOSE` messages.
   - Distinction between user-initiated native `X` close and intentional application exit to prevent shutdown recursion (`_allowClose`).
5. **Visual Styling & Themes**:
   - Fluent / Carbon Studio styling matching application tokens.
   - Full Dark, Light, and High Contrast support via explicit `RequestedTheme` on `ContentDialog`.

### Today + Quick Overlay Final Acceptance Polish (Implemented — Awaiting Final User Acceptance)
Final visual and interaction acceptance refinement across Today and Quick Overlay:
1. **Running Session UI**:
   - Today Hero displays `[ Pause ]` only (centered, clean breathing room).
   - Quick Overlay displays `[ Pause ]` only.
   - The visible `[ Stop ]` button is removed from both Running surfaces.
2. **Paused Session UI**:
   - Displays `[ Continue ]` (primary) and `[ Start New ]` (secondary) only.
   - No visible `[ Stop ]` action.
   - `Continue`: Resumes existing session timer.
   - `Start New`: Finalizes/stops the paused session with actual elapsed active duration and returns the same surface to the Idle launcher.
3. **Session Architecture Preservation**:
   - The underlying Stop/session-finalization architecture, engine APIs, repository methods, and data integrity rules are strictly preserved.
4. **Today Top-Row Geometry & Idle Launcher Polish**:
   - Reduced vertical footprint of both Today Hero and Today Summary cards (~310 DIP height on wide/maximized desktop) eliminating empty bottom dead space while keeping Hero and Summary equal-height siblings.
   - Centered Today Idle Work & Break cards (260×88 DIP tiles with top-left dot/mode and bottom duration) with centered `[ Start ]` button below.
   - Refined Today Summary into a balanced, compact 2×2 metric grid with subtle divider lines.
5. **Quick Overlay Geometry & Proportions**:
   - Compact horizontal desktop flyout (560 DIP width, ~240–250 DIP height).
   - Persistent `FOCUS KEY` header branding with draggable header and close/shortcut buttons.
   - Subheader in active modes shows `• WORK/BREAK SESSION` (left) ... `PAUSED`/`RUNNING` (right).
   - Stretched Work and Break selection cards in Idle mode with top-left dot and right-aligned duration.
   - Large 52 DIP Consolas countdown timer, 6 DIP progress track, centered action buttons, and keyboard hints (`[↵] Continue/Pause`, `[Esc] Close`).
   - Native Win32 dragging, SQLite position persistence, and multi-monitor clamping.

---

## Implemented Work Awaiting Final User Acceptance

### Settings + Quick Overlay Final Refinement (Implemented — Awaiting Final User Acceptance)
Completed the Settings and Quick Overlay experience into a unified native Windows 11 utility:
- **Quick Overlay Visual Refinement**: Draggable shell, multi-monitor clamping, SQLite position persistence.
- **Settings Page Refinements**: `SHORTCUTS` section (Quick Overlay & Open Focus Key with collision detection), `QUICK OVERLAY` section with Reset position, `TIME FORMAT` (12h/24h) preference in Today Activity, and live theme brush refresh on dark/light switch.

---

## Implemented Work Awaiting Final User Acceptance

### Reports Insights Vertical Composition + Today Summary Polish + Yearly Preview Verification (Implemented — Awaiting Final User Acceptance)
- **Reports Insights Vertical Distribution**: Tall Insights rail matches chart height (~380–420 DIP) with its 4 insight groups distributed vertically across proportional `1*` zones with subtle 1px dividers, eliminating bottom dead space.
- **72% / 28% Reports Ratio**: 72% Main Chart / 28% Insights Rail with 16 DIP column spacing gives ~340 DIP width to the rail on standard wide desktop, eliminating text wrapping.
- **Today Summary Card Polish**: Refined internal 2 × 2 metric grid on `TodaySummaryCard` with proportional rows, subtle horizontal divider line, Consolas 26 SemiBold metric values, and clean vertical centering.
- **Yearly Preview Verification**: Verified `FOCUSKEY_YEARLY_PREVIEW=1` development override with synthetic isolated full-year data (12 months in 2025, Jan–Dec) without modifying real data eligibility logic or altering the user's real database.

---

## Implemented Work Awaiting Final User Acceptance

### Today Final Hero Redesign (Implemented — Awaiting User Acceptance)
Redesigned the Today page into a deliberate maximized-desktop composition where the active timer/session area is the clear visual hero:
- **3-Tier Visual Composition**: Quiet Header + Session Hero + Today Summary + Full-width Activity.
- **Shared Session Hero Surface**: Unified surface for Running, Paused, and Idle states.
- **Today Summary Surface**: Single coherent card with a 2 × 2 internal metric grid.
- **Adaptive Breakpoints**: Wide (>= 860 DIP), Medium / Restored (< 860 DIP), Narrow (< 580 DIP).

---

## Implemented Work Awaiting Final User Acceptance

### Yearly Reports (Implemented — Under Acceptance Refinement)
Added a real Yearly Reports experience that integrates into the Reports desktop dashboard.
- **Availability & Eligibility**: Hidden until >= 1 year of history (`earliestDate.AddYears(1) <= today`).
- **Yearly Visualization (Jan → Dec)**: 12 monthly bars, dynamic intervals, compact duration labels.
- **Yearly Summary Metrics & Insights**: Strongest month, active months consistency, prior-year comparison.
- **Year Navigation**: Seamless navigation between eligible years, prevention of navigating into future years.

### Reports Redesign: Cohesive Desktop Dashboard (Implemented — Under Acceptance Refinement)
Redesigned Reports as a cohesive desktop dashboard following the hierarchy:
**Summary → Main Chart → Useful Insights**.
