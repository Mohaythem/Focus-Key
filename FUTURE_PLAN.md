# Focus Key — Future Implementation Plan

This document serves as the persistent repository-level source of truth for all approved future Focus Key work that has not yet been implemented.

---

## Planning Rules for All Future Work

1. **Immediate Synchronization**: Whenever the user approves, changes, removes, postpones, or adds any future Focus Key idea, update `FUTURE_PLAN.md` immediately so it continuously reflects the latest agreed plan.
2. **Completion Tracking**: Whenever a planned item is implemented and accepted, mark it completed or move it to the [Completed Work](#completed-work) section instead of leaving it as pending.
3. **Scope Fidelity**: Do not invent requirements or assume unapproved scope. Only document agreed-upon architectural, functional, and design decisions.

---

## Active Work (Current Roadmap Task)

*None currently active. Ready for next prioritized roadmap item.*

---

## Approved Pending Work

### 1. Window Close (X) Choice Dialog
- When the title bar window close (`X`) button is pressed, present a clear user choice between hiding Focus Key (background shell / system tray) and fully quitting the application.

### 2. Adaptive Navigation & Sidebar Geometry
- Make the sidebar, navigation geometry, and icon presentation adapt more naturally and fluidly across window size changes and responsive tiers.

### 3. Today Active Timer Hero & Lifecycle Actions
- Refine the Today page so the active timer presents as a substantially stronger visual hero.
- Ensure clear, prominent Start / Continue / Stop actions appropriate to the active session state.

---

## Completed Work

### Yearly Reports (Completed: September 2026)
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
  - Truthful observations: strongest focus month (e.g. "August (58h 00m)"), active months consistency (e.g. "9 of 9 months", with monthly average), current streak, work sessions finished, and previous-year comparison (`↑`/`↓`) *only* when prior-year data exists.
- **Year Navigation**:
  - Seamless navigation between eligible years (`<` and `>`), display of calendar year in header and subtitle, and prevention of navigating into future years (`_nextButton` disabled at current year).
- **Responsive & Theme Verification**:
  - Verified across Maximized, Restored (~1000x720), and Minimum (680x500) desktop sizes in Dark, Light, and High Contrast themes.

### Reports Redesign: Cohesive Desktop Dashboard (Completed: September 2026)
Redesigned Reports as a cohesive desktop dashboard following the hierarchy:
**Summary → Main Chart → Useful Insights**.
- **Summary Metrics (Top Row)**: 3 compact primary cards: Focus Time, Work Sessions, and Completion Rate.
- **Dominant Main Chart Hero**: Composes ~75% width on wide/maximized viewports; 5-hour grid interval for Weekly, 10-hour grid interval for Monthly; dynamic Y-axis scaling, substantial bars with rounded tops and exact duration labels.
- **Secondary Insights Rail**: Composes ~25% width on wide windows; truthful contextual insights (period comparisons `↑`/`↓`, active streaks with best badge, strongest focus day/week, active days consistency); calm empty state for zero activity.
- **Responsive Adaptation**: Cleanly reflows into stacked layout on narrower/restored viewports (< 860 DIP) with internal 2-column adaptation (>= 420 DIP).
- **Theme-Aware Visualization**: Fully optimized for Dark, Light, and High Contrast themes using dedicated `ReportsPalette`.
- **Cleanup**: Removed legacy `StreaksCard`, narrative `InsightCard`, and redundant copy.

