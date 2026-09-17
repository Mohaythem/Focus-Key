# Focus Key — Future Implementation Plan

This document serves as the persistent repository-level source of truth for all approved future Focus Key work that has not yet been implemented.

---

## Planning Rules for All Future Work

1. **Immediate Synchronization**: Whenever the user approves, changes, removes, postpones, or adds any future Focus Key idea, update `FUTURE_PLAN.md` immediately so it continuously reflects the latest agreed plan.
2. **Completion Tracking**: Whenever a planned item is implemented and accepted, mark it completed or move it to the [Completed Work](#completed-work) section instead of leaving it as pending.
3. **Scope Fidelity**: Do not invent requirements or assume unapproved scope. Only document agreed-upon architectural, functional, and design decisions.

---

## Active Work (Current Roadmap Task)

*(None currently in progress. Ready for next approved roadmap item.)*

---

## Approved Pending Work

### 1. Yearly Reports View
- Add a Yearly Reports view that remains hidden until the user has accumulated one full year of app usage and session history.
- Automatically expose the view once the one-year threshold is reached, providing long-term productivity and focus retrospectives without cluttering the interface for new users.

### 2. Window Close (X) Choice Dialog
- When the title bar window close (`X`) button is pressed, present a clear user choice between hiding Focus Key (background shell / system tray) and fully quitting the application.

### 3. Adaptive Navigation & Sidebar Geometry
- Make the sidebar, navigation geometry, and icon presentation adapt more naturally and fluidly across window size changes and responsive tiers.

### 4. Today Active Timer Hero & Lifecycle Actions
- Refine the Today page so the active timer presents as a substantially stronger visual hero.
- Ensure clear, prominent Start / Continue / Stop actions appropriate to the active session state.

---

## Completed Work

### Reports Redesign: Cohesive Desktop Dashboard (Completed: September 2026)
Redesigned Reports as a cohesive desktop dashboard following the hierarchy:
**Summary → Main Chart → Useful Insights**.
- **Summary Metrics (Top Row)**: 3 compact primary cards: Focus Time, Work Sessions, and Completion Rate.
- **Dominant Main Chart Hero**: Composes ~75% width on wide/maximized viewports; 5-hour grid interval for Weekly, 10-hour grid interval for Monthly; dynamic Y-axis scaling, substantial bars with rounded tops and exact duration labels.
- **Secondary Insights Rail**: Composes ~25% width on wide windows; truthful contextual insights (period comparisons `↑`/`↓`, active streaks with best badge, strongest focus day/week, active days consistency); calm empty state for zero activity.
- **Responsive Adaptation**: Cleanly reflows into stacked layout on narrower/restored viewports (< 860 DIP) with internal 2-column adaptation (>= 420 DIP).
- **Theme-Aware Visualization**: Fully optimized for Dark, Light, and High Contrast themes using dedicated `ReportsPalette`.
- **Yearly View Preparation**: Header segmented control prepared with `[ Week | Month | Year* ]` (disabled segment with tooltip).
- **Cleanup**: Removed legacy `StreaksCard`, narrative `InsightCard`, and redundant copy.

