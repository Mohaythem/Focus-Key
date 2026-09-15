# Focus Key — Full Visual & UX Audit (Audit Only — Zero Code Changes)

**Project:** Focus Key — Native Windows 11 Focus Utility  
**Technology Stack:** C# / .NET 10 / WinUI 3 / Windows App SDK 1.8 / SQLite  
**Workspace:** `D:\Focus Key`  
**Repository:** `https://github.com/Mohaythem/Focus-Key.git`  
**Branch:** `native/phased-rewrite`  
**Baseline Commit:** `044d1e7`  
**Audit Date:** 2026-09-15  
**Audit Nature:** Strictly AUDIT ONLY — ZERO CODE CHANGES  

---

## 1. Executive Summary & Verdict

Focus Key exhibits exceptional foundational engineering: clean separation of concerns between `FocusKey.Foundation` and `FocusKey.App`, strict SQLite schema migrations, transactional single-instance leasing, robust global hotkey coordination (`Shift + F3`), zero data loss during partial or interrupted sessions, and locale-invariant western Arabic digit formatting (`0-9`).

However, from a visual and UX perspective, the application currently suffers from a critical systemic architectural defect: **unbounded mechanical stretching across large desktop viewports**. When the application is running in its **primary desktop state — Maximized / Full-Screen (1920x1080, 1920x1200, 2560x1440, or 3440x1440)** — the content containers stretch indiscriminately to 1600+ DIP. This unbounded expansion shatters visual hierarchy, detaches interactive controls from their semantic labels by over 700–1400 pixels (violating Fitts's Law and Gestalt Proximity), distorts the 2x2 metric cards into flat 8:1 ribbons, and severely degrades the Reports histogram into a 6.2:1 letterbox strip with 148–238 DIP cavernous empty voids between data bars.

Furthermore, development iteration has left behind visual clutter: the word "Today" appears up to seven times simultaneously on screen, redundant subtitle containers duplicate information within the same card, marketing slogans occupy utility navigation chrome, and keyboard shortcut badges are placed inside launcher cards where they do not function.

### Verdict: REVISION REQUIRED
A targeted visual refinement across six sequential stages (Visual System → Foundation → Today → Reports → Settings + Overlay → Final Consistency QA) is necessary before shipping. **No functional, architectural, or calculation code in `FocusKey.Foundation` requires alteration.** All identified issues are confined to XAML presentation, layout constraints, typographic tokens, microcopy cleanup, and chart geometry.

---

## 2. Viewport Analysis: Three-Tier Responsive Hierarchy

The application was inspected at runtime across three distinct viewport sizes on a native 1920x1200 display:

```
+-----------------------------------------------------------------------------+
| Maximized / Full-Screen Desktop Window (1920x1200) — PRIMARY TARGET         |
| Net Content Area: 1624 DIP wide                                             |
| Issue: Content stretches indefinitely; cards become runways; huge dead gaps.|
+-----------------------------------------------------------------------------+
         |
         v
+-------------------------------------------------------------+
| Normal Restored Desktop Window (~1000x720) — RESPONSIVE     |
| Net Content Area: ~704 DIP wide                             |
| Status: Balanced, dense, legible; optimal chart proportions.|
+-------------------------------------------------------------+
         |
         v
+---------------------------------------------+
| Minimum Supported Window (~680x520) — SAFETY|
| Net Content Area: ~452 DIP wide             |
| Issue: 3-column metric cards clip 28pt font.|
+---------------------------------------------+
```

### 2.1 Maximized / Full-Screen Desktop Window (1920x1200) — Primary Target
- **Geometry:** Sidebar = 216 DIP. Page padding = `(40, 28, 40, 36)`. Usable content width = **1624 DIP**.
- **Root Failure:** In `MainWindow.xaml:35`, `PageContent` (`StackPanel`) specifies `HorizontalAlignment="Stretch"` with **no `MaxWidth`**. All pages (Today, Reports, Settings) stretch mechanically to fill 100% of the display.
- **Visual Impact:**
  - **Today Idle Launcher:** `CurrentCard` spans 1568 DIP. Inside `WorkChoiceCard` (width ~778 DIP), "WORK 30 min" is docked at the far left, while the "Start" button is docked at the far right. A 650 DIP horizontal void divides action from label.
  - **Today Active State:** The `Stop` button is isolated at the far right edge ($x > 1500\text{ DIP}$). The 3 DIP progress bar spans 1568 DIP (ratio > 520:1), looking like an arbitrary window border rule rather than a component progress line.
  - **2x2 Metrics Grid:** Each card spans 806 DIP wide by ~100 DIP high (ratio ~8:1). Numeric text occupies only 140 DIP, leaving over 660 DIP of dead space per card.
  - **Activity Panel:** Column 2 (`1*`) absorbs 1380+ DIP of slack, stranding "Work" on the far left and its duration ("25m 00s") on the far right.
  - **Settings Rows:** Labels are at $x = 16\text{ DIP}$, while toggles/combos/pickers are at $x = 1608\text{ DIP}$. Scanning rows requires uncomfortable horizontal eye movement.
  - **Screen Utilization:** The content occupies only the upper 35% of the vertical space; the lower 65% is an empty black/gray void.

### 2.2 Normal Restored Desktop Window (~1000x720 / Initial 880x660) — Responsive Target
- **Geometry:** Usable content width = **584 to 704 DIP**.
- **Status: Excellent.**
  - Today launcher cards span ~346 DIP each; "WORK 30 min" and "Start" sit within comfortable visual proximity (~220 DIP).
  - 2x2 metric cards have a balanced ~3.5:1 aspect ratio.
  - Reports Chart: Plot width is 610 DIP. Column width is 87 DIP, bar width is 62 DIP (71.2% fill ratio). Aspect ratio is 2.42:1. The chart feels dense, substantial, and cohesive.
  - Settings rows maintain tight coupling between descriptions and controls (~150 DIP gap).

### 2.3 Minimum Supported Window (~680x520) — Safety Boundary
- **Geometry:** In `MainWindow.xaml.cs:137-140`, `ActualWidth < 740` collapses `NavColumn.Width` to 180 DIP and page padding to `(24, 28, 24, 36)`. Usable content width = **452 DIP**.
- **Findings:**
  - **Reports Metric Clipping (P2):** The 3 summary cards divide 452 DIP into 3 columns of 145 DIP each. Minus 40 DIP padding, interior width is **105 DIP**. 28pt Consolas numbers (`"31h 56m"`, `"02h 45m"`) require 115.5 DIP, causing awkward two-line text wrapping or horizontal clipping.
  - **Header Collisions (P2):** In `ReportsView.cs:57-69`, `topHeader` ("Reports" vs segmented selector) lacks a wrap breakpoint, causing the title to crowd the selector at widths below 380 DIP.

---

## 3. Deep Engineering Diagnosis: Maximized Reports Chart Scaling

### 3.1 Mathematical Analysis of the 148–238 DIP Cavernous Void
In `ReportsChart.cs:20-22, 218-233`:
- `PlotAreaHeight` is a fixed constant: **252 DIP** (`TotalPlotHeight = 280 DIP`).
- In Maximized state on 1080p/1200p, plot host width $W \approx \mathbf{1570\text{ DIP}}$.
- Column width: $colW = W / count$.
- Bar width formula:
  ```csharp
  double dynamicBarWidth = Math.Clamp(Math.Floor(colW * 0.72), 24, 76);
  ```

| Mode | Trend Count ($count$) | Column Width ($colW$) | Unclamped 72% Width | Effective Bar Width | Inter-Bar Empty Void | Column Fill Ratio |
|---|---|---|---|---|---|---|
| **Weekly** | 7 days | **224.3 DIP** | 161.5 DIP | **76.0 DIP** | **148.3 DIP** | **33.9%** |
| **Monthly** | 5 weeks | **314.0 DIP** | 226.1 DIP | **76.0 DIP** | **238.0 DIP** | **24.2%** |

**Diagnosis:** The hardcoded `76 DIP` ceiling in `Math.Clamp` was tuned exclusively for a 900–1000 DIP restored window where $colW \approx 87\text{ DIP}$. When the window is maximized, the column expands to 224–314 DIP, but the bar remains pinned at 76 DIP. This leaves **148 to 238 DIP of dead space between bars**. Over 66% to 75% of the chart surface becomes an empty vacuum.

### 3.2 Aspect Ratio Distortion
- Restored window aspect ratio: $610\text{ DIP} / 252\text{ DIP} = \mathbf{2.42 : 1}$ (Balanced).
- Maximized 1080p/1200p aspect ratio: $1570\text{ DIP} / 252\text{ DIP} = \mathbf{6.23 : 1}$ (Severely flattened letterbox).
- Maximized 1440p ultra-wide aspect ratio: $2170\text{ DIP} / 252\text{ DIP} = \mathbf{8.61 : 1}$ (Extreme distortion).
- **Human Factors Failure:** Standard statistical histograms operate between 1.6:1 and 2.5:1. At 6.2:1, the horizontal distance between consecutive bars is up to 10 times greater than the vertical height delta. The eye cannot perform comparative height judgments across such wide horizontal separations without losing vertical reference orientation.

### 3.3 Gridline Matrix Distortion
- Horizontal gridlines are drawn at 5-hour intervals across the 1570 DIP width. For a 10h ceiling, there are only 3 lines spaced vertically by $252 / 2 = 126\text{ DIP}$.
- Vertical separators are drawn every 224 to 314 DIP.
- Both use dashed lines (`StrokeDashArray = { 3, 3 }`, `Opacity = 0.38`).
- Instead of a subtle background texture, the stretched dashed lines divide the screen into gigantic $224 \times 126\text{ DIP}$ or $314 \times 84\text{ DIP}$ rectangles that dominate the visual field.

### 3.4 Engineering & Architectural Remediation Strategy
1. **Shell-Level Content Max-Width (Primary Fix):**
   In `MainWindow.xaml:35`, apply `MaxWidth="920"` and `HorizontalAlignment="Center"` to `PageContent`. At 920 DIP page width, chart plot width is $\approx 826\text{ DIP}$:
   - Weekly: $colW = 826 / 7 = 118\text{ DIP}$; dynamic bar width = 85 DIP; fill ratio = 72%; aspect ratio = 3.2:1.
   - This single constraint immediately cures Today, Reports, and Settings simultaneously.
2. **Proportional Bar Sizing (Defense-in-Depth):**
   In `ReportsChart.cs:228`, eliminate the fixed 76 DIP ceiling or scale it dynamically: `Math.Clamp(Math.Floor(colW * 0.72), 24, Math.Min(colW * 0.75, 120))`.
3. **Responsive Plot Height:**
   Allow plot height to scale with width: `PlotAreaHeight = Math.Clamp(width / 3.0, 240, 320)`.

---

## 4. UI Content & Clutter Audit

Guided by the principle: *"If the UI already communicates it clearly, do not explain it again with text."*

### 4.1 Itemized Content Classification Matrix

| # | Location & Element | Current UI Copy / Container | Proposed Classification | Rationale & Risk Assessment |
|---|---|---|---|---|
| 1 | `MainWindow.xaml:16` | Sidebar Header: `Focus Key` | **[Keep]** | Essential product identity. Zero clutter risk. |
| 2 | `MainWindow.xaml:17-20` | Sidebar Status: `ActiveDot` + `Session active` | **[Keep]** | High-utility collapsed indicator when navigated to Reports/Settings. |
| 3 | `MainWindow.xaml:23-25` | Sidebar Nav: `Today`, `Reports`, `Settings` | **[Keep]** | Primary application navigation. |
| 4 | `MainWindow.xaml:29` | Sidebar: `Quick Overlay` | **[Simplify]** | Integrate `Shift + F3` badge directly into the button row. |
| 5 | `MainWindow.xaml:30` | Sidebar: `Exit Focus Key` | **[Simplify]** | Change to `Exit`. Repeating app name is redundant. |
| 6 | `MainWindow.xaml:31` | Sidebar Footer: `Shift + F3` badge | **[Simplify]** | Move inline next to Quick Overlay button (item 4). |
| 7 | `MainWindow.xaml:31` | Sidebar Footer: `One focused session.` | **[Remove Candidate]** | Pure marketing slogan in utility navigation chrome. Zero usability risk. |
| 8 | `MainWindow.xaml:36` | Page Title: `PageTitle` ("Today" / "Settings") | **[Keep]** | Standard H1 heading. |
| 9 | `MainWindow.xaml:41` | Today Subheader: `DayLabel` ("Tuesday, 15 September 2026") | **[Keep]** | Essential localized date orientation. |
| 10 | `MainWindow.xaml.cs:332` | Tooltip: `"{zone}. Sessions grouped by local start date."` | **[Simplify]** | Shorten to `"{zone}"`. Second sentence is developer jargon. |
| 11 | `MainWindow.xaml:41` | `LoadStatus` ("Loading…", "Starting…", "Stopping…") | **[Keep]** | Accessible live region feedback. |
| 12 | `MainWindow.xaml:42` | Refresh Tooltip: `"Refresh Today"` | **[Simplify]** | Change to `"Refresh"`. "Today" is already established by page context. |
| 13 | `MainWindow.xaml:49` | Active Header: `CURRENT SESSION` | **[Keep]** | Valid when a session is active. |
| 14 | `MainWindow.xaml:56` | Active Subheader: `RunningType` ("Work" / "Break") | **[Simplify]** | Merge into `WORK SESSION` / `BREAK SESSION` to eliminate duplicate header tier. |
| 15 | `MainWindow.xaml:58` | Active Label: `RunningHint` ("remaining") | **[Simplify]** | Capitalize to `"Remaining"`. |
| 16 | `MainWindow.xaml:60` | Active Button: `Stop` | **[Keep]** | Essential action. Standardize with Quick Overlay. |
| 17 | `MainWindow.xaml:73-76` | Idle Header: `CURRENT SESSION  ·  Choose a session` | **[Simplify]** | Change to `START A SESSION`. Stating "CURRENT SESSION" when idle is inaccurate; middle dot is noise. |
| 18 | `MainWindow.xaml:77-79` | Idle Header Badge: `Shift + F3` | **[Remove Candidate]** | Misleading clutter. Shift+F3 summons the Quick Overlay, not the Today card buttons. |
| 19 | `MainWindow.xaml:97-98` | Launcher Card: `WorkChoiceDot` + `WORK` | **[Keep]** | Semantic session identifier. |
| 20 | `MainWindow.xaml:101-102`| Launcher Card: `30` `min` | **[Keep]** | Clear numeric duration. |
| 21 | `MainWindow.xaml:105` | Launcher Card: `Start` button | **[Keep]** | Primary launcher CTA. |
| 22 | `MainWindow.xaml:119-120`| Launcher Card: `BreakChoiceDot` + `BREAK` | **[Keep]** | Semantic session identifier. |
| 23 | `MainWindow.xaml:123-124`| Launcher Card: `10` `min` | **[Keep]** | Clear numeric duration. |
| 24 | `MainWindow.xaml:127` | Launcher Card: `Start` button | **[Keep]** | Primary launcher CTA. |
| 25 | `MainWindow.xaml:136` | Today Metric 1 Subtitle: `today` | **[Remove Candidate]** | Redundant. User is already on the Today page. |
| 26 | `MainWindow.xaml:137` | Today Metric 2 Subtitle: `completed` | **[Simplify]** | Change card title to `Completed Work` without subtitle, or capitalize `Completed`. |
| 27 | `MainWindow.xaml:138` | Today Metric 3 Subtitle: `today` | **[Remove Candidate]** | Redundant. User is already on the Today page. |
| 28 | `MainWindow.xaml:139` | Today Metric 4 Title/Subtitle: `Completion` / `rate` | **[Simplify]** | Change Title to `Completion Rate` and remove `rate` subtitle. Splitting the noun is ungrammatical. |
| 29 | `MainWindow.xaml:143` | Activity Header: `ActivityHeaderButton` | **[Keep]** | Accessible collapse toggle. |
| 30 | `MainWindow.xaml:149` | Activity Header: `ACTIVITY` | **[Keep]** | Standard section heading. |
| 31 | `MainWindow.xaml:155` | Activity Empty State: `"No sessions yet..."` | **[Keep]** | Calm, reassuring empty message. |
| 32 | `MainWindow.xaml.cs:341`| Activity Feed: `"{duration} planned"` | **[Keep]** | Essential indicator for incomplete sessions. |
| 33 | `ReportsView.cs:61` | Reports Header: `Reports` | **[Keep]** | Top-level H1 heading. |
| 34 | `ReportsView.cs:128` | Reports Period Switch: `Weekly`, `Monthly` | **[Keep]** | Core reporting period toggle. |
| 35 | `ReportsView.cs:87, 89`| Nav Glyphs: `‹` and `›` | **[Simplify]** | Replace raw unicode with Segoe Fluent FontIcons (`\uE76B`, `\uE76C`). |
| 36 | `ReportsView.cs:90` | Nav Button: `Today` | **[Simplify]** | Rename to `Current` to prevent confusion with the Today view navigation item. |
| 37 | `ReportsView.cs:91` | Nav Button: `↻` | **[Simplify]** | Replace raw unicode with Segoe Fluent FontIcon `\uE72C`. |
| 38 | `ReportsView.cs:193` | Reports Subtitle: `"{rangeText}  ·  {timeZone}"` | **[Keep]** | Essential reporting period context. |
| 39 | `ReportsView.cs:198-200`| Reports Metric Subtitles: `"work sessions"`, etc. | **[Keep]** | Useful secondary context distinguishing work vs break. |
| 40 | `ReportsView.cs:245` | Streak Left Subtitle: `"active focus streak"` | **[Remove Candidate]** | 100% self-evident from title `Current Streak`. |
| 41 | `ReportsView.cs:282` | Streak Right Subtitle: `"all-time best streak"` | **[Remove Candidate]** | 100% self-evident from title `Longest Streak`. |
| 42 | `ReportsView.cs:302` | Chart Card Title: `Focus Activity` | **[Keep]** | Standard card heading. |
| 43 | `ReportsView.cs:306-309`| Chart Subtitle: `"Last 7 days (today on the right)"` | **[Keep]** | Provides immediate chart orientation. |
| 44 | `ReportsView.cs:315` | Chart Legend: Swatch + `Focus Time` | **[Keep]** | Color legend for chart bars. |
| 45 | `ReportsView.cs:326-351`| Chart Bottom Strip: `"Showing the last 7 days..."` | **[Remove Candidate]** | Exact 1:1 duplication of header subtitle 200px above. Delete container. |
| 46 | `ReportsView.cs:360` | Insight Header: `INSIGHT` | **[Keep]** | Section heading. |
| 47 | `ReportsView.cs:374-412`| Insight Copy: Automated summary sentences | **[Keep]** | High analytical value. |
| 48 | `ReportsChart.cs:197` | Chart Empty Notice: `"No focus activity recorded..."` | **[Keep]** | Clear centered empty state. |
| 49 | `SettingsView.cs:128` | Row Subtitle: `"Controls whether the application..."` | **[Simplify]** | Subtitle is verbose. Shorten to `"System, light, or dark mode"`. |
| 50 | `SettingsView.cs:129` | Row Subtitle: `"Enhances text legibility..."` | **[Simplify]** | Shorten to `"Increase text and border contrast"`. |
| 51 | `SettingsView.cs:137-140`| Light Theme Subtitles: `"Light page...", "Light primary..."` | **[Simplify]** | Under "LIGHT THEME", repeating "Light" 3x is redundant. Strip prefix. |
| 52 | `SettingsView.cs:148-151`| Dark Theme Subtitles: `"Dark page...", "Dark primary..."` | **[Simplify]** | Under "DARK THEME", repeating "Dark" 3x is redundant. Strip prefix. |
| 53 | `SettingsView.cs:160-161`| Session Color Subtitles: `"Used for work session..."` | **[Remove Candidate]** | Self-evident under "SESSION COLORS". |
| 54 | `SettingsView.cs:168` | Sounds Subtitle: `"Play a soft tick on start..."` | **[Simplify]** | Shorten to `"Play start tick and completion chime"`. |
| 55 | `SettingsView.cs:174` | Shortcut Subtitle: `"Global keyboard shortcut"` | **[Keep]** | Explains system-wide background availability. |
| 56 | `SettingsView.cs:179` | System Subtitle: `"Launch Focus Key automatically..."` | **[Simplify]** | Strip trailing period to match other row descriptions. |
| 57 | `SettingsView.cs:184` | Data Import Row: 3x "Import history" echo | **[Simplify]** | Button to `"Import…"`, description to `"Tab-delimited website CSV"`. |
| 58 | `SettingsView.cs:185` | Data Export Row: 3x "Export history" echo | **[Simplify]** | Button to `"Export…"`, description to `"Export records to tab-delimited CSV"`. |
| 59 | `SettingsView.cs:530` | Settings Status: `"Changes saved automatically..."` | **[Simplify]** | Remove persistent footer text; show transiently on save or simplify to `"All changes saved."`. |
| 60 | `QuickOverlayWindow.xaml:24` | Overlay Header: `FOCUS KEY` | **[Keep]** | Minimal branding. |
| 61 | `QuickOverlayWindow.xaml:38` | Overlay Badge: `Shift + F3` | **[Remove Candidate]** | Redundant. User just pressed Shift+F3 to open overlay; key is non-functional inside. |
| 62 | `QuickOverlayWindow.xaml:68, 97` | Overlay Cards: `Work`, `Break` | **[Keep]** | Core choices. |
| 63 | `QuickOverlayWindow.xaml:114`| Overlay Button: `Start` | **[Keep]** | Primary action. |
| 64 | `QuickOverlayWindow.xaml:128`| Overlay Mode: `WORK SESSION` | **[Keep]** | Mode heading. |
| 65 | `QuickOverlayWindow.xaml:165`| Overlay Stop Button: `Stop Session` | **[Simplify]** | Change to `Stop` to match MainWindow. |
| 66 | `QuickOverlayWindow.xaml:187-254`| Overlay Hints: `[←→] Select`, `[↵] Start`, `[Esc] Close` | **[Simplify]** | Standardize notation: `[← →] Select`, `[Enter] Start`, `[Esc] Close`. |
| 67 | `TodayFormatting.cs:56` | Launcher Formatter: Compound Duration | **[Simplify]** | Fix compound formatting `30:15 min` to clean `30m 15s`. |

---

## 5. Comprehensive Audit Dimensions

### Dimension A: Visual Hierarchy & Page Composition
- **Page Titles:** Divergence between pages. `Today` and `Settings` use the shell `PageTitle` TextBlock (`MainWindow.xaml:36`). `Reports` hides the shell title and embeds its own custom Grid header (`ReportsView.cs:57`). This causes slight alignment and padding mismatches between tabs.
- **Card Boxing & Elevation:** All cards use a single flat style `FkCard` (`Background="{ThemeResource FkSurface}"`, `BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"`, `BorderThickness="1"`). There is no subtle elevation or layering to distinguish primary hero interaction surfaces from static summary cards.
- **Active State Visual Dominance:** In active mode, `CurrentCard` paints its entire background with the solid session color (`Work` teal or `Break` violet). In Dark theme, this creates an enormous, high-luminance colored rectangle that overpowers all other visual elements. A subtle tinted surface with a prominent accent edge/progress track would feel far calmer and more aligned with Fluent design.

### Dimension B: Responsive & Full-Screen Behavior
- **Stretching Failure:** Detailed in Section 2. The app completely lacks a desktop max-width strategy.
- **Horizontal Eye Fatigue:** The distance between row labels and action buttons across 1624 DIP exceeds 30 cm on a 27-inch monitor, requiring unnatural head movement.
- **Empty Void Distribution:** Content is stacked vertically in the top portion of the screen, leaving vast empty regions below. A centered max-width presentation resolves this cleanly without complex reflowing.

### Dimension C: Typography & Information Hierarchy
- **Font Family Schism:**
  - Standard text uses `Segoe UI Variable Text`.
  - Metrics, timers, and chart numbers use `Consolas` (`FkMetricText`).
  - **Divergence:** In `MainWindow.xaml:101-102`, Today launcher duration numbers use `FontFamily="Segoe UI Variable"`, `FontSize="26"`, while Today metric cards use `Consolas 30pt`, Reports metrics use `Consolas 28pt`, and Streaks use `Consolas 20pt`.
- **Typographic Scale:** Font sizes currently span: 10pt (hints), 11pt (dim/section), 12pt (muted/body), 13pt (standard), 15pt (chart title), 16pt (page title), 20pt (streaks), 24pt (overlay timer), 26pt (launcher duration), 28pt (reports metric), 30pt (today metric), 36pt (overlay active timer), 40pt (today active timer). This 13-step scale lacks a formal typographic ramp and should be consolidated.

### Dimension D: Spacing, Geometry & Alignment
- **Fragmented Corner Radii:**
  - `3 DIP`: Swatches (`App.xaml:61`), overlay shortcut badge (`QuickOverlayWindow.xaml:35`).
  - `4 DIP`: Navigation buttons (`App.xaml:55`), activity dot, chart bars.
  - `5 DIP`: Segment container (`App.xaml:63`), launcher start button (`App.xaml:117`).
  - `6 DIP`: Standard cards (`FkCard`, `App.xaml:53`), overlay surface (`QuickOverlayWindow.xaml:15`).
  - `8 DIP`: Launcher choice cards (`MainWindow.xaml:88, 110`).
- **Recommendation:** Standardize on a disciplined 3-tier radius system:
  - **4 DIP (Small):** Interactive controls, buttons, toggles, badges, swatches.
  - **6 DIP (Medium):** Cards, flyouts, containers.
  - **8 DIP (Large):** Hero surfaces, overlay window shell.

### Dimension E: Surfaces, Colors, Contrast & Theming
- **Dark Theme (Carbon Studio):** Excellent contrast and calmness. Background `#121212`, Sidebar `#181818`, Surface `#1E1E1E`, Surface2 `#252525`. Contrast ratios for primary text (`#E0E0E0`) exceed 14:1.
- **Light Theme Contrast Defect (F-07):**
  - Background `#F2F5F5`, Foreground `#0F1414`.
  - `FkDim` is defined as `#8A9898` (`App.xaml:24`).
  - On `#F2F5F5`, `#8A9898` has a contrast ratio of **only 2.5:1**, failing WCAG AA requirements (minimum 4.5:1 for body/captions). Small 10–11pt text styled with `FkDimText` is washed out in bright ambient light.
  - **Fix:** Darken light theme `FkDim` to `#657575` (contrast ratio > 4.6:1).
- **High Contrast Theme:** Correctly implements Windows system colors (`SystemColorWindowColor`, `SystemColorWindowTextColor`, `SystemColorHighlightColor`).

### Dimension F: Controls & Interaction States
- **Touch Target Deficit:**
  - Settings preset color swatches (`SettingsView.cs:625-634`) have a bounding box of `20x20 DIP`.
  - Reports date nav buttons (`ReportsView.cs:470-474`) are `32x32 DIP`.
  - Both fall well below the Windows touch accessibility baseline of $40 \times 40\text{ DIP}$.
- **Focus Visuals:**
  - Quick Overlay cards use `UseSystemFocusVisuals="True"` with custom brush styling.
  - MainWindow launcher cards (`WorkChoiceCard`, `BreakChoiceCard`) are `Border` controls wrapped with pointer event handlers rather than native `Button` controls, lacking built-in keyboard tab stops and focus rectangles.

### Dimension G: Motion & State Transitions
- **Accessibility Discipline:** Both `MainWindow.xaml.cs:431` and `QuickOverlayWindow.xaml.cs:283` query `UISettings.AnimationsEnabled` before invoking storyboards, respecting user preferences for reduced motion.
- **Layout Stability:** `CurrentCard` sets `MinHeight="180"`, preventing layout jumps when transitioning between Idle and Active states.
- **Future Polish:** State transitions are currently abrupt (instant visibility swaps). A subtle 150–200ms opacity cross-fade during page navigation and mode switching will dramatically increase tactile feel.

### Dimension H: Cross-Page & System Consistency
- **Button Conventions:** Today uses filled accent buttons; Reports uses outline/transparent segmented buttons; Settings uses standard Windows settings row controls.
- **Card Padding:** Today `SummaryGrid` cards use `Padding="22,24"`; `CurrentCard` uses `Padding="28,24"`; Reports summary cards use `Padding="20,16"`; Settings cards use `Padding="0"` with `Padding="16,14"` on rows. These should be unified to a coherent token scale (`Compact: 16`, `Standard: 20`, `Hero: 24`).

---

## 6. Prioritized Issue Log

### Systemic Issues (Affect Multiple Surfaces)

| ID | Severity | Area / File | Observation | Why it Weakens the Product | Recommended Design Direction | Implementation Stage |
|---|---|---|---|---|---|---|
| **SYS-01** | **High (P1)** | `MainWindow.xaml:35`<br>`PageContent` | Absence of `MaxWidth` constraint causes unbounded stretching across 1624+ DIP in full-screen. | Creates cavernous horizontal dead space, shatters visual balance, detaches actions from labels by 700–1400 DIP. | Apply `MaxWidth="920"` and `HorizontalAlignment="Center"` to `PageContent`. | Visual System / Shell |
| **SYS-02** | **High (P1)** | `ReportsChart.cs:20-22, 228`<br>`ReportsChart` | Fixed 252 DIP height + hardcoded 76 DIP bar cap produces 6.2:1 aspect ratio and 148–238 DIP voids. | Graph looks broken and sparse in maximized windows; bars look like isolated slivers in an empty field. | Uncap bar width scaling (`Math.Min(colW * 0.75, 120)`), allow dynamic height, constrain via page max-width. | Reports Refinement |
| **SYS-03** | **Medium (P2)**| `App.xaml:24`<br>`FkDim` Brush | Light theme `FkDim` (`#8A9898`) on `#F2F5F5` has contrast ratio of 2.5:1 (fails WCAG AA 4.5:1). | Subtitles and captions are difficult to read in light theme under office lighting. | Darken Light `FkDim` to `#657575` (contrast ratio 4.6:1). | Foundation / Theming |
| **SYS-04** | **Medium (P2)**| Across `App.xaml`, `MainWindow.xaml`, `SettingsView.cs` | Fragmented corner radius values (3, 4, 5, 6, 8 DIP) used inconsistently. | Visual dissonance; subtle lack of craft and cohesion across neighboring components. | Consolidate to 3-tier token: Small (4 DIP), Medium (6 DIP), Large (8 DIP). | Visual System Tokens |
| **SYS-05** | **Low (P3)** | `MainWindow.xaml:101`<br>vs `App.xaml:48` | Font family divergence: Segoe UI Variable for Today launcher vs Consolas for metrics. | Lack of typographic system; numbers appear in mismatched styles across adjacent cards. | Unify all quantitative/temporal metrics under a shared monospaced numeric token. | Visual System Tokens |

### Page-Specific Issues

| ID | Severity | Area / File | Observation | Why it Weakens the Product | Recommended Design Direction | Implementation Stage |
|---|---|---|---|---|---|---|
| **PG-01** | **High (P1)** | `SettingsView.cs:891-927`<br>`SettingsView.Row` | Rows stretch to 1624 DIP, placing toggles/inputs 1400 DIP away from their labels. | Severe violation of Gestalt proximity; users cannot track which control belongs to which row. | Inherits fix from `SYS-01` (`MaxWidth="920"`). Also enforce internal max-width on rows. | Settings Refinement |
| **PG-02** | **Medium (P2)**| `ReportsView.cs:196-202`<br>`ReportsView.Metric` | 3-column metric cards on narrow viewports (<740 DIP) shrink to 105 DIP, clipping 28pt Consolas text. | Text truncation and visual defect on smaller laptop screens or split-screen windows. | Add responsive font scaling or wrap to 2 columns on narrow viewports. | Reports Refinement |
| **PG-03** | **Medium (P2)**| `MainWindow.xaml:77-79`<br>`HeroOverlayShortcutHint` | `Shift + F3` badge displayed in Today idle launcher card header. | Misleads users into believing `Shift + F3` starts the card; shortcut actually opens Quick Overlay. | Remove shortcut badge from the launcher card header. | Today Refinement |
| **PG-04** | **Medium (P2)**| `ReportsView.cs:326-351`<br>`ChartCard.infoStrip` | Dedicated border container at bottom of chart repeats header subtitle verbatim. | Pure visual bloat and duplication inside a single component. | Remove bottom `infoStrip` container entirely. | Reports Refinement |
| **PG-05** | **Medium (P2)**| `MainWindow.xaml:31`<br>`SidebarOverlayShortcutHint` | Marketing tagline `"One focused session."` stacked below shortcut badge in sidebar. | Adds unnecessary text and an extra divider border to the primary navigation chrome. | Remove tagline; move `Shift + F3` badge inline next to "Quick Overlay" button. | Today / Shell Refinement |
| **PG-06** | **Low (P3)** | `MainWindow.xaml:136, 138`<br>`SummaryGrid` | Subtitle `"today"` repeated on Focus Time and Break Time cards. | Semantic over-saturation of "Today" on the Today page. | Remove `"today"` subtitle from metric cards. | Today Refinement |
| **PG-07** | **Low (P3)** | `MainWindow.xaml:139`<br>`SummaryGrid` | Compound noun split: Title `"Completion"`, Subtitle `"rate"`. | Grammatically awkward; inconsistent with Reports page `"Completion Rate"`. | Change Title to `"Completion Rate"`, remove subtitle. | Today Refinement |
| **PG-08** | **Low (P3)** | `SettingsView.cs:184-186`<br>`SettingsView.data` | Triple echo of `"Import history"` / `"Export history"` across label, button, and description. | Repetitive visual clutter in settings rows. | Shorten button to `"Import…"`, description to `"Tab-delimited website CSV"`. | Settings Refinement |
| **PG-09** | **Low (P3)** | `QuickOverlayWindow.xaml:187-254` | Footer keyboard hints mix glued arrows (`←→`), return symbol (`↵`), and abbreviation (`Esc`). | Visual inconsistency and dissonance in key representation. | Standardize to: `[← →] Select`, `[Enter] Start`, `[Esc] Close`. | Quick Overlay Refinement |
| **PG-10** | **Low (P3)** | `ReportsView.cs:87-91`<br>`NavButton` | Nav buttons use raw unicode characters (`‹`, `›`, `↻`) instead of Segoe Fluent Icon glyphs. | Visual mismatch with the rest of the application's native Segoe icons. | Replace with standard FontIcon glyphs (`\uE76B`, `\uE76C`, `\uE72C`). | Reports Refinement |

---

## 7. Prioritized Refinement Map (Roadmap for Future Stages)

This roadmap organizes the resolution of all audit findings into six strictly sequenced, non-overlapping future implementation stages:

```
+--------------------------------------------------------------------------------+
| Stage 1: Visual System & Design Tokens                                         |
| • Formalize 3-tier corner radius tokens (4, 6, 8 DIP) [SYS-04]                 |
| • Standardize typographic ramp and numeric metric tokens [SYS-05]              |
| • Define spacing and card padding tokens (Compact 16, Standard 20, Hero 24)    |
+--------------------------------------------------------------------------------+
                                       |
                                       v
+--------------------------------------------------------------------------------+
| Stage 2: Foundation & Theme Engine                                             |
| • Correct Light theme FkDim contrast to #657575 (WCAG AA compliant) [SYS-03]   |
| • Fix TodayFormatting compound duration formatting (30m 15s)                   |
| • Ensure all theme palettes expose standardized tokens                         |
+--------------------------------------------------------------------------------+
                                       |
                                       v
+--------------------------------------------------------------------------------+
| Stage 3: Today Page & Application Shell Refinement                             |
| • Enforce PageContent MaxWidth="920" & Center alignment [SYS-01]               |
| • Today Hero Launcher: Change idle header to START A SESSION, remove dot &     |
|   misleading Shift+F3 badge [PG-03], remove "Choose a session"                 |
| • Metric Cards: Remove redundant "today" subtitles, fix "Completion Rate"      |
| • Sidebar: Remove "One focused session." [PG-05], inline Shift+F3 on Overlay   |
| • Standardize Activity column widths to prevent excessive gap on wide viewports|
+--------------------------------------------------------------------------------+
                                       |
                                       v
+--------------------------------------------------------------------------------+
| Stage 4: Reports Page & Responsive Chart Refinement                            |
| • ReportsChart: Uncap 76 DIP bar clamp; dynamically scale bar width [SYS-02]   |
| • Standardize plot aspect ratio (~2.5:1) via max-width container               |
| • Metric Cards: Add responsive text scaling/wrapping to prevent clipping [PG-02|
| • Clutter Removal: Delete duplicate bottom infoStrip container [PG-04]         |
| • Streaks Card: Remove redundant subtitles ("active focus streak", etc.)       |
| • Navigation: Replace raw unicode with Segoe Fluent FontIcons [PG-10]          |
+--------------------------------------------------------------------------------+
                                       |
                                       v
+--------------------------------------------------------------------------------+
| Stage 5: Settings Page & Quick Overlay Refinement                              |
| • Settings Rows: Benefit from MaxWidth=920, eliminating 1400 DIP gaps [PG-01]  |
| • Microcopy: Strip repetitive "Light"/"Dark" prefixes, simplify Data 3x echo   |
| • Touch Targets: Expand color preset swatches to minimum 36x36 DIP bounding box|
| • Quick Overlay: Remove redundant header Shift+F3 badge; standardize stop copy |
|   to "Stop"; harmonize footer keyboard hint notation [PG-09]                   |
+--------------------------------------------------------------------------------+
                                       |
                                       v
+--------------------------------------------------------------------------------+
| Stage 6: Final Consistency QA & Windows Installer                              |
| • Verify all surfaces across Maximized, Restored, and Minimum viewports        |
| • Verify Dark, Light, and High Contrast themes                                 |
| • Full automated regression test suite execution                               |
| • Build clean Release installer FocusKeySetup.exe                              |
+--------------------------------------------------------------------------------+
```

---

## 8. Safety & Verification Evidence

### 8.1 Zero Code Changes Verification
- **Source Code Alteration:** Exactly ZERO lines of C# source code, XAML, project files, or database schema were modified.
- **Phase Integrity:** No new numbered phase document (e.g. `Phase 14.md`) was created. Existing phase documents (`Phase 13.md`, etc.) remain completely untouched.
- **Engine Preservation:** The single authoritative session engine, background timer, SQLite persistence, and transactional hotkey registration were strictly unmolested.

### 8.2 Git Status & Repository Hygiene
- Working branch: `native/phased-rewrite`
- Baseline commit: `044d1e7`
- The only file introduced into the repository is this audit document: `D:\Focus Key\VISUAL_AUDIT.md`.
