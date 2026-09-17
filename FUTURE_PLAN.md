# Focus Key — Future Implementation Plan

This document serves as the persistent repository-level source of truth for all approved future Focus Key work that has not yet been implemented.

---

## Planning Rules for All Future Work

1. **Immediate Synchronization**: Whenever the user approves, changes, removes, postpones, or adds any future Focus Key idea, update `FUTURE_PLAN.md` immediately so it continuously reflects the latest agreed plan.
2. **Completion Tracking**: Whenever a planned item is implemented and accepted, mark it completed or move it to the [Completed Work](#completed-work) section instead of leaving it as pending.
3. **Scope Fidelity**: Do not invent requirements or assume unapproved scope. Only document agreed-upon architectural, functional, and design decisions.

---

## Active Work (Current Roadmap Task)

*None active. Awaiting user visual acceptance and next goal.*

---

## Approved Pending Work

### 1. Window Close (X) Choice Dialog
- When the title bar window close (`X`) button is pressed, present a clear user choice between hiding Focus Key (background shell / system tray) and fully quitting the application.

---

## Implemented Work Awaiting Final User Acceptance

### Settings + Quick Overlay Final Refinement (Implemented — Awaiting Final User Acceptance)
Completed the Settings and Quick Overlay experience into a unified native Windows 11 utility:
- **Quick Overlay Visual Refinement**:
  - Consistent compact window shell (480 DIP width, ~280–340 DIP height) across Idle, Running, and Paused states.
  - Idle state launcher: Work & Break selection cards using configured durations from Settings, subtle semantic accents, primary `[ Start ]` action.
  - Running state: Dominant 52 DIP timer typography in Consolas font, 6 DIP semantic progress bar, primary `[ Pause ]` (elevated) and secondary `[ Stop ]` (neutral) actions.
  - Paused state: Frozen timer, primary `[ Continue ]` (elevated), `[ Start New ]` (neutral), and `[ Stop ]` actions.
  - Light mode consistency matching Carbon Studio tokens and contrast standards.
  - Esc key dismisses Overlay across all states without stopping the session; small Close (`×`) button in quiet draggable header.
- **Draggable Quick Overlay & Position Persistence**:
  - Movable by mouse via header region (native Win32 `ReleaseCapture` + `SendMessage WM_NCLBUTTONDOWN HTCAPTION`).
  - Persist last valid screen position to SQLite settings repository (`overlay_position_x`, `overlay_position_y`, Migration 11).
  - Multi-monitor and off-screen recovery: DPI-aware clamping against available monitor work areas (`OverlayPositionHelper.ClampToWorkAreas`) ensuring an always-reachable header.
  - Default first opening centered on foreground/active monitor (`OverlayPositionHelper.CalculateInitialCenter`).
  - Position stability: Window does not jump/recenter across Idle → Running → Paused → Continue → Start New state transitions.
- **Settings Page Refinements**:
  - Two clean shortcut rows inside `SHORTCUTS` section (`Quick Overlay`, `Open Focus Key`) with conflict detection, duplicate prevention, and rollback.
  - `QUICK OVERLAY` section with `Reset position` button (clears custom coordinates back to default centered behavior).
  - `TIME FORMAT` section: User-selectable 12-hour (`9:05 AM`) / 24-hour (`09:05`) clock format preference applied consistently to wall-clock timestamps (Today Activity, session history) via `TodayFormatting.FormatClockTime` while preserving countdown durations.
  - Fixed live theme-refresh issues when toggling Dark ↔ Light in Settings so all shortcut, position, and selector controls update dynamically without stale brushes or restart (`SettingsView.RefreshVisuals`).

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
- **3-Tier Visual Composition**:
  1. Header: Quiet "Today" page title + date subtitle without motivational clutter.
  2. Main Row: Session Hero (~2/3 horizontal width) + Today Summary (~1/3 horizontal width) with ~20-24 DIP separation on maximized desktop (1240 DIP max-width).
  3. Activity Section: Full-width "TODAY'S ACTIVITY" table below with compact desktop rows and clean empty state.
- **Shared Session Hero Surface (~380 DIP min height on wide)**:
  - **Running State**: `WORK SESSION` / `BREAK SESSION` label with colored active dot, `RUNNING` status badge, dominant 80 DIP countdown typography + `remaining` subtext, thin 6 DIP semantic progress bar, `[ Pause ]` (primary elevated) and `[ Stop ]` (secondary neutral) action buttons.
  - **Paused State**: Same physical hero region, `PAUSED` status badge, frozen countdown + `paused` subtext, progress bar, `[ Continue ]` (primary elevated) and `[ Start New ]` (secondary neutral) action buttons.
  - **Idle State**: Same physical hero region, `START A SESSION` label, `Ready when you are` prompt, substantial Work & Break selector cards using actual configured durations and restrained semantic accents (no solid saturated backgrounds), `[ Start ]` action button.
- **Today Summary Surface**:
  - Single coherent card with a 2 × 2 internal metric grid (Focus Time, Work Sessions, Break Time, Completion Rate) with Consolas 28 SemiBold values.
- **Adaptive Breakpoints**:
  - Wide (>= 860 DIP available / >= 1100 DIP window): Hero (2/3) + Summary (1/3) side-by-side, Activity full width below, 80 DIP timer font.
  - Medium / Restored (< 860 DIP available): Hero full width, Summary below Hero (2x2 grid), Activity below Summary, 68 DIP timer font.
  - Narrow (< 580 DIP available): Single vertical column flow with 56 DIP timer font, no clipping or horizontal overflow.


### Reports Acceptance Refinement & Temporary Yearly Preview (Implemented — Under Acceptance)
- Streaks presentation redesigned into a 2-column group with balanced Current and Longest Streak metrics (no emojis).
- Redundant session count metrics removed from the insights rail across all periods.
- Symmetrical 4-item consistency and average metrics across Weekly, Monthly, and Yearly.
- Temporary `FOCUSKEY_YEARLY_PREVIEW=1` override added for pre-1-year visual inspection (to be removed after user visual acceptance).

---

## Implemented Work Awaiting Final User Acceptance

### Yearly Reports (Implemented — Under Acceptance Refinement)
Added a real Yearly Reports experience that integrates into the Reports desktop dashboard.
- **Availability & Eligibility**:
  - Week and Month remain normally available.
  - Year remains completely hidden until >= 1 year of usable Focus Key history has accumulated (`earliestDate.AddYears(1) <= today`).
  - Eligibility is derived directly from persisted native sessions and imported historical focus dates, surviving restarts without arbitrary UI flags.
  - Ineligible users see only `[ Week | Month ]` without visual clutter or disabled buttons.
- **Yearly Visualization (Jan → Dec)**:
  - 12 monthly bars (Jan → Dec) representing real accumulated focus duration.
  - Tailored geometry, responsive bar scaling, and dynamic Y-axis intervals (10h, 20h, 50h, 100h) producing 4 to 6 legible grid lines.
  - Compact duration labels formatted above bars (`FormatYearlyBarDuration`) and bold emphasis on the current month column.
- **Yearly Summary Metrics**:
  - Preserves standard Focus Key metrics: Focus Time, Work Sessions, Completion Rate.
  - Native sessions provide session counts and completion rates; imported history contributes strictly to Focus Time without fabricating fake sessions or distorting completion rate.
- **Yearly Contextual Insights**:
  - Truthful observations: strongest focus month (e.g. "August (58h 00m)"), active months consistency (e.g. "9 of 9 months", with monthly average), and previous-year comparison (`↑`/`↓`) *only* when prior-year data exists.
- **Year Navigation**:
  - Seamless navigation between eligible years (`<` and `>`), display of calendar year in header and subtitle, and prevention of navigating into future years (`_nextButton` disabled at current year).

### Reports Redesign: Cohesive Desktop Dashboard (Implemented — Under Acceptance Refinement)
Redesigned Reports as a cohesive desktop dashboard following the hierarchy:
**Summary → Main Chart → Useful Insights**.
- **Summary Metrics (Top Row)**: 3 compact primary cards: Focus Time, Work Sessions, and Completion Rate.
- **Dominant Main Chart Hero**: Composes ~75% width on wide/maximized viewports; 5-hour grid interval for Weekly, 10-hour grid interval for Monthly; dynamic Y-axis scaling, substantial bars with rounded tops and exact duration labels.
- **Secondary Insights Rail**: Composes ~25% width on wide windows; truthful contextual insights (period comparisons `↑`/`↓`, active streaks, strongest focus day/week, active days consistency); calm empty state for zero activity.
- **Responsive Adaptation**: Cleanly reflows into stacked layout on narrower/restored viewports (< 860 DIP) with internal 2-column adaptation (>= 420 DIP).
- **Theme-Aware Visualization**: Fully optimized for Dark, Light, and High Contrast themes using dedicated `ReportsPalette`.
- **Cleanup**: Removed legacy `StreaksCard`, narrative `InsightCard`, and redundant copy.

