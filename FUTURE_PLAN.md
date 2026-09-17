# Focus Key — Future Implementation Plan

This document serves as the persistent repository-level source of truth for all approved future Focus Key work that has not yet been implemented.

---

## Planning Rules for All Future Work

1. **Immediate Synchronization**: Whenever the user approves, changes, removes, postpones, or adds any future Focus Key idea, update `FUTURE_PLAN.md` immediately so it continuously reflects the latest agreed plan.
2. **Completion Tracking**: Whenever a planned item is implemented and accepted, mark it completed or move it to the [Completed Work](#completed-work) section instead of leaving it as pending.
3. **Scope Fidelity**: Do not invent requirements or assume unapproved scope. Only document agreed-upon architectural, functional, and design decisions.

---

## Active Work (Current Roadmap Task)

*None currently active. Awaiting user acceptance of the Today + Quick Overlay Final Acceptance Polish milestone.*

---

## Approved Pending Work

### 1. Window Close (X) Choice Dialog
- When the title bar window close (`X`) button is pressed, present a clear user choice between hiding Focus Key (background shell / system tray) and fully quitting the application.

---

## Implemented Work Awaiting Final User Acceptance

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
