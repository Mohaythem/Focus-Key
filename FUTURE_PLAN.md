# Focus Key — Future Implementation Plan

This document serves as the persistent repository-level source of truth for all approved future Focus Key work that has not yet been implemented.

---

## Planning Rules for All Future Work

1. **Immediate Synchronization**: Whenever the user approves, changes, removes, postpones, or adds any future Focus Key idea, update `FUTURE_PLAN.md` immediately so it continuously reflects the latest agreed plan.
2. **Completion Tracking**: Whenever a planned item is implemented and accepted, mark it completed or move it to the [Completed Work](#completed-work) section instead of leaving it as pending.
3. **Scope Fidelity**: Do not invent requirements or assume unapproved scope. Only document agreed-upon architectural, functional, and design decisions.

---

## Active Work (Current Roadmap Task)

### Reports Acceptance Refinement & Temporary Yearly Preview (In Progress)
Refining the Reports desktop dashboard and Yearly Reports experience for final user visual acceptance:
- **Insights Rail Refinement**:
  - Redesign streak presentation into a clear "Streaks" group with distinct, balanced statistics for **Current Streak** and **Longest Streak**.
  - Remove redundant insights (such as repeating session counts already visible in summary metric cards above).
  - Provide truthful, non-duplicative consistency metrics (e.g. active focus days/months with period averages where meaningful).
  - Polish visual engineering: label/value hierarchy, dividers, vertical rhythm, spacing, native Fluent styling (remove emoji glyphs).
- **Temporary Yearly Preview Override (`FOCUSKEY_YEARLY_PREVIEW=1`)**:
  - Temporary development and testing aid allowing the user to inspect the Yearly Reports view with real partial-year data before accumulating one full calendar year.
  - Keeps domain eligibility invariant and persisted data untouched.
  - **Must be removed after final user visual acceptance.**

---

## Approved Pending Work

### 1. Window Close (X) Choice Dialog
- When the title bar window close (`X`) button is pressed, present a clear user choice between hiding Focus Key (background shell / system tray) and fully quitting the application.

### 2. Today Active Timer Hero & Lifecycle Actions
- Refine the Today page so the active timer presents as a substantially stronger visual hero.
- Ensure clear, prominent Start / Continue / Stop actions appropriate to the active session state.

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

