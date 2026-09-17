# Focus Key — Project Progress & State Checkpoint

## Current Goal
Reports Acceptance Refinement & Temporary Yearly Preview Override completed and verified.

## Current Checkpoint
Reports Acceptance Refinement and Temporary Yearly Preview Override are 100% implemented and verified:
- **Insights Rail Refinement**:
  - Streak presentation redesigned into a deliberate 2-column "Streaks" group with distinct, balanced statistics for **Current Streak** and **Longest Streak** (Consolas 20 SemiBold, no fire emoji 🔥, no tiny subtext caption).
  - Redundant insights (e.g. "Work sessions finished" repeating top summary metrics) eliminated from the insights rail across all periods.
  - Symmetrical 4-item truthful insights suite across all reporting periods:
    - Weekly: Period Comparison (`↑`/`↓`), Streaks group (Current & Longest), Strongest Day, Active focus days + daily average.
    - Monthly: Period Comparison (`↑`/`↓`), Streaks group (Current & Longest), Strongest Week, Active focus weeks + weekly average.
    - Yearly: Prior-year Comparison (if prior data exists), Streaks group (Current & Longest), Strongest Month, Active focus months + monthly average.
- **Temporary Yearly Preview Override (`FOCUSKEY_YEARLY_PREVIEW=1`)**:
  - When `FOCUSKEY_YEARLY_PREVIEW=1` (or `"true"`), the `Year` selector appears and is interactive even if the user has < 1 full year history.
  - When unset/default, production behavior remains completely untouched (`Year` is completely hidden until real domain eligibility is met).
  - Domain eligibility invariant (`ReportsService.IsYearEligible`), persisted data, and reporting calculations remain pure and untouched.
  - Note: Will be removed upon final user visual acceptance.
- **Automated Tests**: 715/715 unit tests passing (`dotnet test -c Release`), 0 failed, 0 skipped.
- **Build**: 0 Warning(s), 0 Error(s) (`dotnet build -c Release`).
- **Visual Verification**: Authoritative runtime inspection completed across Maximized Dark/Light, Restored Dark (~1000x720), Minimum size (680x500), Ineligible state (`[ Week | Month ]`), Ineligible state with preview override (`[ Week | Month | Year ]`), and Weekly/Monthly regressions.

## Completed Work
- **Visual & UX Audit**: Comprehensive desktop/full-screen audit documented in `VISUAL_AUDIT.md`.
- **Visual System Definition**: Carbon Studio / Fluent design system specified in `VISUAL_SYSTEM.md`.
- **Stage 1 (Foundation)**: Design tokens, contrast corrections, typography styles, and 3-tier geometry (`b52919f`).
- **Stage 2 (Today Page)**: Responsive 880 DIP centering, idle hero launcher, active session zero-layout-jump presentation, cleaned metric titles, collapsible activity section (`7c94c90`).
- **Stage 3 (Navigation Shell, Session Behavior & Window Management)**:
  - Destructive Stop interaction replaced with non-destructive Pause / Continue / Start New lifecycle.
  - Minimize to tray with single-instance activation and global hotkey restoration.
  - Dual global shortcuts (`Shift + F3` Quick Overlay, `Shift + F4` Open Focus Key) with SQLite persistence and conflict management (`4e5e783`).
- **Stage 4 (Reports Redesign: Cohesive Desktop Dashboard)**: Dominant chart hero (~75%), 3 compact summary metrics, secondary contextual insights rail (~25%), reflow to stacked, truthful period comparison & insights.
- **Stage 5 (Yearly Reports)**: Data-driven eligibility, 12-month calendar aggregation, dynamic yearly ceiling/intervals, truthful prior-year comparison and insights, and future navigation clamping.
- **Stage 5b (Reports Acceptance Refinement & Temporary Preview)**: Balanced streaks group, zero metric repetition, comprehensive consistency metrics across all periods, and temporary `FOCUSKEY_YEARLY_PREVIEW` override.

## Remaining Work
- **Stage 6: Approved Pending Items from FUTURE_PLAN.md**
  - Item 1: Window Close (X) Choice Dialog (hide to background tray vs. quit).
  - Item 2: Today Active Timer Hero & Lifecycle Actions.
- **Stage 7: Settings Page & Quick Overlay Refinement**
  - Settings Page: Constrain container to 880 DIP max-width, center on desktop, expand color preset click targets (36x36 DIP), clean microcopy echoes.
  - Quick Overlay: Clean floating card hierarchy, standardize Stop action, harmonize keyboard hints (`[← →]`, `[Enter]`, `[Esc]`).
- **Stage 8: Final Consistency QA & Windows Packaging**
  - Full-surface visual regression audit (Maximized, Restored, Minimum; Dark, Light, High Contrast).
  - Package clean standalone Release installer (`FocusKeySetup.exe`).

## Important Active Decisions
- **Year Eligibility Invariant**: Derived strictly from real data (`earliestDate.AddYears(1) <= today`), surviving app restarts without arbitrary UI flags. Ineligible users see only `[ Week | Month ]`.
- **Zero Fabrication**: Imported historical focus contributes to focus duration and historical span, never fabricating fake session counts, completion numbers, or completion rates.
- **Yearly Grid Step**: `ComputeYearlyStepHours` dynamically adapts grid ticks to 10h, 20h, 50h, or 100h based on max monthly volume.
- **Prior-Year Comparison Invariant**: Prior-year focus delta is shown only when real prior-year data exists (`PreviousPeriodDuration > 0`).

## Last Verification
- **Build**: `dotnet build -c Release` (0 Warnings, 0 Errors).
- **Test Suite**: `dotnet test -c Release` (715 passed, 0 failed, 0 skipped).
- **Visual Captures**:
  - `reports_year_maximized_dark.png`: 75%/25% side-by-side, 12 monthly bars (Jan-Dec), 20h intervals (0h-60h), balanced streaks group, no redundancy, disabled Next button.
  - `reports_year_maximized_light.png`: High-contrast dark typography on white cards, forest teal bars.
  - `reports_year_restored_dark.png`: Stacked responsive reflow at ~1000x720 DIP.
  - `reports_year_minimum_dark.png`: Minimum size (680x500 DIP) boundary check, intact sidebar and wrapping header.
  - `reports_ineligible_week_dark.png`: Ineligible user state verified with `[ Week | Month ]` (Year button completely absent).
  - `reports_ineligible_preview_enabled_dark.png`: Ineligible user with `FOCUSKEY_YEARLY_PREVIEW=1` verified with `[ Week | Month | Year ]` (Year active & interactive).
  - `reports_week_regression_dark.png`: Weekly view regression verified intact with balanced streaks group and active days consistency.
  - `reports_month_regression_dark.png`: Monthly view regression verified intact with balanced streaks group and active weeks consistency.

## Current Git State
- Branch: `native/phased-rewrite`
- Working tree contains complete, verified Reports Acceptance Refinement & Temporary Yearly Preview implementation.
