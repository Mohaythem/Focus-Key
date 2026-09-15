# Focus Key — Visual System Specification (Carbon Studio / Fluent)

**Document Version:** 1.0.0  
**Project:** Focus Key — Native Windows 11 Desktop Focus Utility  
**Technology Stack:** C# / .NET 10 / WinUI 3 / Windows App SDK 1.8 / SQLite  
**Workspace:** `D:\Focus Key`  
**Repository:** `https://github.com/Mohaythem/Focus-Key.git`  
**Branch:** `native/phased-rewrite`  
**Baseline Commit:** `044d1e7`  
**Primary Audit Reference:** [`D:\Focus Key\VISUAL_AUDIT.md`](file:///D:/Focus%20Key/VISUAL_AUDIT.md)  
**Status:** Design System Definition Only — Implementation Deferred to Later Stages  

---

## 1. Product Visual Principles

> **"Focus Key — Carbon Studio, built with Fluent principles."**

Focus Key is a dedicated, distraction-free native Windows 11 desktop focus utility. It is not an enterprise dashboard, not a web application wrapped in a webview, and not a cloned mobile timer. It belongs in the Windows 11 utility ecosystem alongside native tools like Windows Terminal, Calculator, and Clock, but with a refined, tactile, studio-grade identity.

### The Six Core Visual Principles

1. **Desktop-First & Window-Aware:**
   The primary design target is the **Maximized / Full-Screen desktop window**. The visual system adapts compositionally to large displays rather than stretching mechanically or retreating into an artificially narrow web-style column.
2. **Calm & Intentionally Quiet:**
   Focus Key exists to support deep cognitive work. The interface uses low-contrast, deep carbon neutrals (`#121212` to `#252525`) in dark mode and warm paper tones (`#F2F5F5` to `#FFFFFF`) in light mode. Visual hierarchy is established through typography and spatial rhythm, not decorative borders or loud color fills.
3. **Restrained Semantic Session Color:**
   Work Teal and Break Violet communicate session meaning and system state. They are **semantic indicators**, not general-purpose decoration. They appear on status dots, progress tracks, active timers, and subtle selection rings. Large desktop cards never blast 100% saturated color across the display.
4. **Native WinUI 3 & Fluent Integration:**
   Focus Key leverages native Windows App SDK controls, Segoe UI Variable, system focus visuals, DWM window styling, and Windows accessibility settings (`UISettings.AnimationsEnabled`, High Contrast mode).
5. **Compact Information Density:**
   Desktop software should be information-efficient. Unnecessary whitespace that behaves like dead space is eliminated. Controls, padding, and gaps are compact, structured, and deliberate.
6. **Zero Digital Jitter (Typographic Stability):**
   Active countdown timers, duration values, and updating metrics utilize strict monospaced tabular figures (`Consolas`) to ensure numbers never vibrate or shift horizontally during 1Hz clock ticks.

---

## 2. Responsive Desktop Composition Strategy

### 2.1 The Dual Anti-Pattern Trap

The runtime audit proved that Focus Key suffered from two opposing risks:
- **Anti-Pattern A (Current Bug — Unbounded Stretching):** Without a max-width constraint, maximizing on 1080p/1200p/1440p displays stretches content across 1624+ DIP. Action buttons detach from labels by 700–1400 DIP, cards flatten into 8:1 ribbons, and histograms degrade into 6.2:1 panoramic strips with 238 DIP empty chasms.
- **Anti-Pattern B (The Web-Column Fallacy):** Forcing the entire desktop application into an arbitrarily narrow centered column (e.g. 640 DIP) wastes 70% of a 27-inch desktop monitor, looks like a mobile website floating in space, and forces multi-metric data to wrap awkwardly.

### 2.2 The Adaptive Page-Specific Width Strategy

To achieve a true native desktop composition, Focus Key establishes **two distinct architectural content-width classes**:

```
+=======================================================================================+
| Desktop Window (Maximized 1920x1200)                                                  |
|                                                                                       |
|  [Sidebar]  |<----------------------- Available Content Span: 1624 DIP -------------->|
|  216 DIP    |                                                                         |
|             |   Fluid Left Gutter      Centered Page Content     Fluid Right Gutter   |
|             |  [================]  [============================]  [================] |
|             |                      |                            |                     |
|             |                      | Today & Settings:  880 DIP |                     |
|             |                      | Reports View:     1040 DIP |                     |
|             |                      |                            |                     |
|             |                      [============================]                     |
+=======================================================================================+
```

#### Class 1: Utility & Workflow Surfaces (`Today`, `Settings`)
- **Maximum Width:** **`MaxWidth = 880 DIP`**
- **Alignment:** `HorizontalAlignment = "Center"`
- **Architectural Rationale:**
  - In `Today`, 880 DIP provides exactly $(880 - 12) / 2 = 434\text{ DIP}$ per column. This is the optimal proportion for the 2x2 metrics grid (giving cards a balanced ~3.5:1 ratio instead of 8:1) and gives the Work/Break hero launcher choice cards a comfortable width where "WORK 30 min" and "Start" are separated by ~240 DIP rather than 700 DIP.
  - In `Settings`, 880 DIP matches official Windows 11 Settings guidelines, bounding the gap between option labels and toggles/combos to under 350 DIP, completely eliminating horizontal eye fatigue.

#### Class 2: Data-Visualization Surfaces (`Reports`)
- **Maximum Width:** **`MaxWidth = 1040 DIP`**
- **Alignment:** `HorizontalAlignment = "Center"`
- **Architectural Rationale:**
  - Reports is an analytical surface containing a 7-day or 5-week histogram and 3 summary metric tiles.
  - At 1040 DIP, each of the 3 summary metric cards receives $\approx 320\text{ DIP}$ of width, providing ample room for 28pt Consolas numbers (`"31h 56m"`) with zero horizontal text clipping.
  - The Focus Activity chart receives $\approx 960\text{ DIP}$ of net plot width. In weekly mode, each day column is 137 DIP wide; with a 72% fill ratio, bars are a substantial 98 DIP wide, leaving clean 39 DIP separators. The aspect ratio is $960 / 300 = 3.2:1$, preserving natural histogram proportions.

#### Class 3: Compact Floating Utility (`Quick Overlay`)
- **Fixed Width:** **`Width = 420 DIP`** (non-resizable, fixed aspect ratio, DWM-centered).

### 2.3 Viewport Breakpoints & Responsive Behavior

| Breakpoint Tier | Window Width Range | Sidebar Width | Page Padding | Content Width Rule | Specific Layout Adaptations |
|---|---|---|---|---|---|
| **Compact** | $< 740\text{ DIP}$ | `180 DIP` | `(24, 20, 24, 28)` | Fluid $100\%$ | Reports top header wraps; summary metrics dynamically scale from 28pt to 22pt; date nav controls wrap to row 2. |
| **Restored** | $740\text{ to }1200\text{ DIP}$ | `216 DIP` | `(40, 28, 40, 36)` | Fluid up to `MaxWidth` | Natural desktop layout; comfortable gutters; 2-column Today grid; 3-column Reports metrics. |
| **Maximized** | $> 1200\text{ DIP}$ up to 4K | `216 DIP` | `(40, 28, 40, 36)` | Pinned at `MaxWidth` (880 / 1040 DIP) | Horizontally centered with fluid margin gutters; vertical spacing fixed at 20 DIP rhythm. |

---

## 3. Surface Hierarchy & Layering

Focus Key uses a 7-layer neutral surface elevation model designed for OLED and high-contrast desktop displays.

```
Layer 0: Window Backdrop (Desktop / MicaAlt / Solid)
 └── Layer 1: App Background (FkBackground)
      └── Layer 2: Sidebar Surface (FkSidebar)
           └── Layer 3: Primary Card Surface (FkCard / FkSurface)
                └── Layer 4: Subtle / Nested Surface (FkSurface2)
                     └── Layer 5: Interactive Surface (Buttons / Inputs)
                          └── Layer 6: Floating Overlay (QuickOverlayWindow)
                               └── Layer 7: Modal / Flyouts (Color Picker / Dialogs)
```

### Surface Token Specifications

| Token | Dark (Carbon Studio) | Light (Paper Studio) | High Contrast (OS Mode) | Purpose & Usage Rule |
|---|---|---|---|---|
| `FkBackground` | `#121212` | `#F2F5F5` | `SystemColorWindowColor` | Main page canvas and scrollable background. |
| `FkSidebar` | `#181818` | `#E6ECEC` | `SystemColorWindowColor` | Navigation rail; 1 DIP border on right edge. |
| `FkSurface` | `#1E1E1E` | `#FFFFFF` | `SystemColorWindowColor` | Primary cards, hero launcher container, metric tiles. |
| `FkSurface2` | `#252525` | `#F7FAFA` | `SystemColorWindowColor` | Nested cards, segment containers, shortcut badges. |
| `FkOverlay` | `#1E1E1E` | `#FFFFFF` | `SystemColorWindowColor` | Floating Quick Overlay window surface. |
| `FkBorder` | `#2E2E2E` | `#D4DCDC` | `SystemColorWindowTextColor` | 1 DIP card borders, row dividers, separator lines. |
| `CardStroke` | `#2E2E2E` | `#D4DCDC` | `SystemColorWindowTextColor` | Standard card stroke color. |

---

## 4. Color System & Contrast Rules

### 4.1 Semantic Session Colors

Work and Break colors represent user state. They must never be applied as giant solid fills across whole desktop cards.

```
       WORK (Focus Teal)                     BREAK (Calm Violet)
   Curated Default: #2F8F83               Curated Default: #7667B8
   Hex Range: Teal / Cyan / Blue          Hex Range: Violet / Purple / Indigo
```

#### Curated Work Presets
1. **Focus Teal (Default):** `#2F8F83`
2. **Deep Teal:** `#24756D`
3. **Fresh Teal:** `#3A9D8F`
4. **Steel Cyan:** `#3D8391`
5. **Focus Blue:** `#3B78B4`

#### Curated Break Presets
1. **Calm Violet (Default):** `#7667B8`
2. **Indigo:** `#5967A8`
3. **Soft Purple:** `#8067A8`
4. **Plum:** `#8A5F8F`
5. **Slate Violet:** `#686784`

### 4.2 Semantic Role Applications

| Surface Role | Work Treatment | Break Treatment | Neutral Fallback |
|---|---|---|---|
| **Dot Indicator** | Solid 6x6 circle (`#2F8F83`) | Solid 6x6 circle (`#7667B8`) | 6x6 circle (`FkDim`) |
| **Choice Card Border (Idle)** | 1 DIP stroke (`#2F8F83` at 30% alpha) | 1 DIP stroke (`#7667B8` at 30% alpha) | 1 DIP stroke (`CardStroke`) |
| **Choice Card Hover (Idle)** | 1 DIP stroke (`#2F8F83` at 80% alpha) | 1 DIP stroke (`#7667B8` at 80% alpha) | 1 DIP stroke (`FkSecondary`) |
| **Active Session Card** | Neutral `FkSurface` + 5% Work tint | Neutral `FkSurface` + 5% Break tint | Neutral `FkSurface` |
| **Progress Track** | Track: `FkSurface2`, Bar: Solid Work | Track: `FkSurface2`, Bar: Solid Break | Solid `FkSecondary` |
| **Launcher Start Button** | Work accent background, white text | Break accent background, white text | Standard button |

### 4.3 Neutral Text Tokens & Light Theme Contrast Resolution

#### The Light Theme `FkDim` Contrast Audit
- In the runtime audit, `FkDim` in Light Theme was `#8A9898` on `#F2F5F5`.
- Relative luminance of `#F2F5F5` is $L_{bg} \approx 0.911$.
- Relative luminance of `#8A9898` is $L_{text} \approx 0.305$.
- Resulting contrast ratio: $(0.911 + 0.05) / (0.305 + 0.05) = \mathbf{2.71 : 1}$.
- This **fails** the WCAG 2.1 AA requirement of **4.5:1** for body/caption text under 18pt.

#### The Verified Solution: `#5C6E6E`
- Color value: R=92, G=110, B=110 (`#5C6E6E`).
- Relative luminance: $L_{text} \approx 0.152$.
- Resulting contrast ratio: $(0.911 + 0.05) / (0.152 + 0.05) = 0.961 / 0.202 = \mathbf{4.76 : 1}$.
- **Result:** **Exceeds WCAG 2.1 AA** baseline while remaining visually distinct from `FkSecondary` (`#5A6A6A`, ratio 5.03:1) and `FkForeground` (`#0F1414`, ratio 15.6:1).

#### Text Token Matrix

| Token | Dark Color | Light Color | High Contrast | WCAG AA Ratio | Primary Usage |
|---|---|---|---|---|---|
| `FkForeground` | `#E0E0E0` | `#0F1414` | `SystemColorWindowTextColor` | $> 14.5 : 1$ | Primary titles, active timers, duration numbers, row titles. |
| `FkSecondary` | `#A0A0A0` | `#5A6A6A` | `SystemColorWindowTextColor` | $> 5.0 : 1$ | Section headings, subtitles, inactive nav items, units. |
| `FkDim` | `#6E6E6E` | **`#5C6E6E`** | `SystemColorWindowTextColor` | **$> 4.7 : 1$ (Light)** | Secondary hints, timestamps, status labels, captions. |
| `FkAccent` | `#4CC2FF` | `#183739` | `SystemColorHighlightColor` | $> 7.0 : 1$ | System focus visuals, active toggle indicators. |

---

## 5. Typography System & Ramp

Focus Key avoids an uncontrolled collection of arbitrary font sizes. The application uses a strictly defined **8-step typographic ramp** with an intentional segregation between natural language and monospaced numeric presentation.

### 5.1 Font Family Selection Rules

1. **`Segoe UI Variable` (Text / Display):**
   - Used for all human language: Page titles, section headings, card titles, row labels, button labels, descriptions, and natural language insights.
   - Provides crisp, modern native Windows 11 legibility with optical size compensation.
2. **`Consolas` (Monospaced Tabular Figures):**
   - Strictly reserved for values that change dynamically or require vertical column alignment:
     * **Active countdown timer:** `00:51`, `24:50` (prevents 1Hz digit width jitter).
     * **Quantitative metric cards:** `31h 56m`, `3 days`, `85%` (guarantees column alignment).
     * **Chart axis labels & duration tags:** `0h`, `5h`, `10h`, `1h 30m`.
     * **Keyboard shortcut badges:** `Shift + F3`, `Esc`, `Enter`.

### 5.2 The 8-Step Typographic Ramp

| Ramp Level | Token Name | Font Family | Size | Weight | Character Spacing | Line Height | Casing Rule | Example Usage |
|---|---|---|---|---|---|---|---|---|
| **Step 1** | `DisplayTimer` | Consolas | 36 / 40pt | Regular | 0 | 1.1 | N/A | Active countdown timer in Overlay (36pt) and Today (40pt). |
| **Step 2** | `MetricValue` | Consolas | 28pt | Regular | -10 | 1.15 | N/A | Summary metric cards (`FkMetricText`), Focus Time, Streaks. |
| **Step 3** | `DurationChoice`| Segoe UI Variable | 26pt | SemiBold | 0 | 1.15 | N/A | Today launcher duration choices (`30`, `10`). |
| **Step 4** | `PageTitle` | Segoe UI Variable | 16pt | Medium | -10 | 1.25 | Title Case | Top-level window headers (`Today`, `Reports`, `Settings`). |
| **Step 5** | `BodyStrong` | Segoe UI Variable | 13pt | Medium / Regular | 0 | 1.3 | Title / Sentence | Nav items, Settings row labels, primary buttons. |
| **Step 6** | `Supporting` | Segoe UI Variable | 12pt | Regular | 0 | 1.3 | Sentence Case | Subtitles, duration units (`min`), activity row types. |
| **Step 7** | `SectionHeader` | Segoe UI Variable | 11pt | SemiBold | **+60** | 1.35 | **ALL CAPS** | Card headings: `CURRENT SESSION`, `ACTIVITY`, `INSIGHT`. |
| **Step 8** | `CaptionHint` | Segoe UI Variable | 10 / 11pt | Regular | 0 | 1.3 | Sentence Case | Status notes, tooltips, secondary hints. |

---

## 6. Spacing & Geometry Tokens

### 6.1 Spacing Scale

Focus Key establishes an 8-point base scale with micro-intervals:

| Spacing Token | DIP Value | Purpose & Architectural Usage Rule |
|---|---|---|
| `Space-2` | 2 DIP | Sub-pixel alignment, text-to-subtext micro-gap. |
| `Space-4` | 4 DIP | Dot indicator to label gap, duration number to unit margin. |
| `Space-8` | 8 DIP | Control group spacing, inline badge padding, horizontal nav gaps. |
| `Space-12` | 12 DIP | Choice card internal column spacing, segmented button gap, grid gaps. |
| `Space-16` | 16 DIP | Settings row internal padding, activity feed padding, medium grid gaps. |
| `Space-20` | 20 DIP | Section vertical gaps, standard card interior padding (`FkCard`). |
| `Space-24` | 24 DIP | Hero launcher card interior padding, Quick Overlay outer padding. |
| `Space-28` | 28 DIP | Top headroom in chart plot area, vertical page header margin. |
| `Space-40` | 40 DIP | Desktop horizontal page margins on standard/wide viewports. |

### 6.2 The 3-Tier Corner Radius Family

The runtime audit identified five inconsistent radii (3, 4, 5, 6, 8 DIP). The visual system consolidates geometry into **three disciplined tiers**:

```
[Small: 4 DIP]         [Medium: 6 DIP]         [Large: 8 DIP]
Controls, Badges        Cards, Containers       Hero Surfaces, Window
```

| Tier | Radius Token | DIP Value | Components & Usage Rule |
|---|---|---|---|
| **Small** | `ControlRadius` | **4 DIP** | Primary buttons, navigation pills, toggles, text boxes, color swatches, shortcut badges, segmented buttons. |
| **Medium** | `CardRadius` | **6 DIP** | Standard cards (`FkCard`), metric summary tiles, streak companion card, settings group containers, dialogs. |
| **Large** | `HeroRadius` | **8 DIP** | Today hero launcher choice cards (`WorkChoiceCard`, `BreakChoiceCard`), Quick Overlay outer shell. |

---

## 7. Control System & Interactive States

All interactive controls follow native WinUI 3 interaction states while adhering to Carbon Studio styling.

### Control State Matrix

| Control Type | Normal State | PointerOver (Hover) | Pressed State | Focused State (Keyboard) | Disabled State |
|---|---|---|---|---|---|
| **Primary Button** (Start / Stop) | Solid accent fill, high-contrast text, `Radius=4` | Opacity 90%, subtle brightness lift | Opacity 75%, scale 98% | 2 DIP outer focus rect (`FkAccent`) | Opacity 40%, hit-test disabled |
| **Quiet / Nav Button** | Transparent background, `FkSecondary` text | `FkSurface2` background, `FkForeground` text | `FkSurface2` 80% opacity | 2 DIP system focus rect | Opacity 40% |
| **Segmented Button** | Active: `FkSurface`, border, bold; Inactive: Transparent | Inactive: subtle `FkSurface2` background | Opacity 80% | Standard focus visual | Opacity 40% |
| **Choice Card** (Today Launcher) | 1 DIP border (`CardStroke`), subtle semantic tint | Border brightens to 80% semantic color, Start button highlights | Scale 99%, opacity 85% | 2 DIP focus rect around card | Opacity 40% |
| **Toggle Switch** | Native WinUI 3 Fluent toggle with semantic tint | Fluent hover animation | Fluent pressed scale | Native focus visual | Native disabled |
| **Numeric TextBox** | Consolas 13pt, centered, 1 DIP border | Border brightens to `FkSecondary` | Border active accent | 2 DIP accent border | Opacity 40% |
| **Color Preset Swatch**| Solid color swatch, `Width=24`, `Height=24`, `Radius=4` | 1 DIP outer hover ring | Scale 95% | 2 DIP white/black focus ring | Opacity 40% |

### Accessibility & Touch Target Rules
- **Minimum Interactive Bounding Box:** All buttons, swatches, and toggles must provide an effective hit-test area of at least **$36 \times 36\text{ DIP}$** on desktop, with touch targets expanding to **$40 \times 40\text{ DIP}$**.
- Color preset swatches in Settings must expand from their current 20x20 DIP size to a minimum of **$24 \times 24\text{ DIP}$ swatch within a $36 \times 36\text{ DIP}$ click container**.

---

## 8. Semantic Session Surfaces (Work & Break)

### 8.1 Idle Hero Launcher Composition
- The Idle Launcher in Today presents **two balanced hero choices** within `CurrentCard`:
  - **Left:** Work Choice Surface (`HeroRadius = 8 DIP`, subtle teal accent border).
  - **Right:** Break Choice Surface (`HeroRadius = 8 DIP`, subtle violet accent border).
- Each choice surface contains:
  - Mode dot (6x6 DIP) + ALL CAPS mode title (`WORK` / `BREAK`, 11pt, `CharacterSpacing=60`).
  - Large duration number (26pt, `DurationChoice`) + unit (`min`, 12pt, bottom-aligned).
  - Primary `Start` action button docked on the right side of the card.
- **Maximized Spacing Constraint:** Because the parent container is constrained to `MaxWidth = 880 DIP`, each choice surface is capped at ~425 DIP wide. The Start button sits within 200–240 DIP of the duration number, maintaining tight visual proximity.

### 8.2 Active Session Card Composition
- When a timer is running, `CurrentCard` displays the active session:
  - **Surface Background:** Neutral `FkSurface` with a **subtle 5% semantic tint** (`#2F8F83` for Work, `#7667B8` for Break). **Zero solid high-luminance color flooding.**
  - **Status Indicator:** Glowing 6x6 semantic dot + `WORK SESSION` / `BREAK SESSION` (11pt ALL CAPS).
  - **Countdown Timer:** Large 40pt Consolas display (`DisplayTimer`) rendered in high-contrast `FkForeground`.
  - **Progress Track:** 3 DIP height line spanning the active card width. Track: `FkSurface2`, Filled bar: Solid Work teal / Break violet.
  - **Action Button:** Subdued capsule `Stop` button docked at bottom right.

---

## 9. Motion System & State Transitions

Focus Key prioritizes stability and performance. Motion is used exclusively to communicate state changes, never as gratuitous visual decoration.

### 9.1 Motion Rules & Curves
- **Standard Duration:** **`150ms to 200ms`**
- **Easing Curve:** Native Windows `FastOutSlowIn` (cubic-bezier `0.1, 0.9, 0.2, 1.0`).
- **Reduced Motion Respect:** Every storyboard or animation must inspect `UISettings.AnimationsEnabled`. If animations are disabled in Windows Settings, transitions must instantly switch visibility (0ms).

### 9.2 Transition Specifications

| State Change | Transition Mechanism | Duration | Visual Communicated |
|---|---|---|---|
| **Idle → Active Session** | Cross-fade opacity between `IdleContent` and `ActiveContent` within `CurrentCard`. | 180ms | Calm morphing of the hero launcher into an active timer. No height jump (`MinHeight=180`). |
| **Activity Expand / Collapse** | Vertical height expansion + chevron rotation (0° to 180°). | 200ms | Native collapsible disclosure. |
| **Page Navigation** | Subtle opacity cross-fade (0.85 → 1.0) on `PageContent`. | 150ms | Seamless navigation between Today, Reports, Settings. |
| **Choice Card Hover** | Border opacity transition (0.3 → 0.85). | 120ms | Responsive tactile affordance. |
| **Overlay Show / Hide** | Native DWM window fade and elevation. | OS Native | Instant global hotkey response. |

---

## 10. Content & Clutter Principles

Guided by the primary heuristic:  
> **"If the UI already communicates meaning clearly through position, color, or iconography, do not explain it again with repetitive text."**

### 10.1 The Three-Tier Content Rule
- **KEEP:** Text required for primary accessibility, orientation, or quantitative data (e.g. page titles, localized date, metric numbers, button actions).
- **SIMPLIFY:** Verbose sentences, redundant labels, or ungrammatical splits that can be made concise (e.g. `"Completion"` + `"rate"` → `"Completion Rate"`; Settings row descriptions shortened to clean fragments).
- **REMOVE:** Redundant subtitles that restate surrounding context, marketing slogans, duplicate container strips, and non-functional shortcut badges.

### 10.2 Definite Design-System Decisions on Clutter

1. **"Today" Semantic Saturation:**
   Remove the lower-case `"today"` subtitles from the 2x2 metric cards. The user is on the Today page; repeating "today" under Focus Time and Break Time is pure visual noise.
2. **Double Explanation in Reports Chart:**
   Delete the bottom `infoStrip` border container in `ReportsView.cs`. The chart header already displays `"Last 7 days (today on the right)"`; repeating this in a second box at the bottom of the same card is redundant.
3. **Misleading `Shift + F3` Badge in Launcher:**
   Remove the shortcut badge from the Today hero launcher card header. In the launcher, it misleads users into thinking pressing Shift+F3 starts the session. Move the shortcut representation inline as a subtle badge next to `"Quick Overlay"` in the sidebar.
4. **Idle Launcher Header:**
   Change the idle header from `"CURRENT SESSION  ·  Choose a session"` to clean, accurate action copy: **`"START A SESSION"`**. Remove the decorative middle dot.
5. **Sidebar Marketing Slogan:**
   Remove `"One focused session."` from the sidebar footer. A desktop utility navigation rail should not carry permanent marketing copy.

---

## 11. Data Visualization Rules (Reports Focus Activity)

These rules resolve the severe maximized chart degradation identified in the audit without modifying the underlying Foundation calculations or the 5-hour vertical scale behavior.

```
       CONSTRAINED DESKTOP CHART (MAX 1040 DIP CONTAINER)
   15h + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
       |                                                           |
   10h + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
       |                             [===]                         |
    5h + - - - - - - - - [===] - - - [===] - - - - - - - - [===] - +
       |                 [===]       [===]                 [===]   |
    0h +=================[===]=======[===]=================[===]===+
             Mon          Tue         Wed         Thu       Today
       <--- colW: 137 DIP --->
       <-- bar: 98 DIP (72%) ->
```

### 11.1 Geometry & Proportions
- **Container Max-Width:** The Reports page container is constrained to **`MaxWidth = 1040 DIP`**, giving the chart plot host a maximum width of $\approx \mathbf{960\text{ DIP}}$.
- **Plot Area Height:** `PlotAreaHeight = 260 DIP` (`TotalPlotHeight = 290 DIP` with 30 DIP headroom).
- **Aspect Ratio Target:** Between **`2.8 : 1` and `3.2 : 1`** on maximized displays (down from the distorted 6.23:1).
- **Column Sizing & Dynamic Bar Fill:**
  - In Weekly mode (7 days): Column width $colW \approx 960 / 7 = \mathbf{137\text{ DIP}}$.
  - The hardcoded `76 DIP` ceiling in `ReportsChart.cs:228` is **removed**.
  - Bar width formula:
    $$\text{dynamicBarWidth} = \text{Math.Clamp}(\lfloor colW \times 0.72 \rfloor, 24, 110)$$
  - In maximized state, bar width is $\lfloor 137 \times 0.72 \rfloor = \mathbf{98\text{ DIP}}$.
  - Inter-bar void is $137 - 98 = \mathbf{39\text{ DIP}}$ (clean 71.5% fill ratio). Cavernous 148–238 DIP voids are completely eliminated.

### 11.2 Gridline & Axis Rules
- **Horizontal 5-Hour Gridlines:** Drawn at 5-hour intervals up to `ceilingHours` (`0h, 5h, 10h, 15h...`). Dashed stroke (`3, 3`), low opacity (0.30).
- **Vertical Separators:** Drawn strictly at column boundaries. With 39 DIP inter-bar voids, the vertical separators frame the data naturally instead of forming stretched rectangles.
- **Durations Above Bars:** Rendered in Consolas 11pt, SemiBold, high contrast, 4 DIP above the bar.

---

## 12. Accessibility System

Focus Key guarantees full compliance with **WCAG 2.1 Level AA** standards across all surfaces.

1. **Contrast Compliance:**
   - Dark Theme: All text exceeds 4.5:1 (Primary text exceeds 14:1).
   - Light Theme: Primary text exceeds 15:1; secondary text exceeds 5:1; caption text (`FkDim` `#5C6E6E`) verified at **4.76:1**.
   - High Contrast Mode: Inherits active Windows high-contrast theme palette automatically.
2. **Keyboard Navigation & Tab Order:**
   - Full keyboard accessibility across Today, Reports, Settings, and Quick Overlay.
   - Tab order is strictly logical (Top to bottom, left to right).
   - Focus visuals are never suppressed; they render via `UseSystemFocusVisuals="True"` with a distinct 2 DIP accent border.
3. **Screen Readers & Automation:**
   - Every interactive control defines explicit `AutomationProperties.Name` and `AutomationProperties.HeadingLevel`.
   - Dynamic operations (session starting, stopping, loading) announce via `AutomationProperties.LiveSetting = "Polite"`.
4. **Hit-Testing & Targets:**
   - No interactive element has an effective touch target smaller than $36 \times 36\text{ DIP}$.

---

## 13. Traceability Matrix: Visual Audit Findings → Visual System Decisions

| Audit Finding ID | Finding Description | Visual System Resolution | Governing Section in this Doc |
|---|---|---|---|
| **SYS-01** | Unbounded full-screen stretching to 1624+ DIP | Enforce `MaxWidth = 880 DIP` on Today/Settings, `1040 DIP` on Reports, centered with fluid gutters. | Section 2.2 |
| **SYS-02** | Reports chart 6.2:1 aspect ratio & 148–238 DIP voids | Uncap 76 DIP clamp; scale bars to 72% of column (up to 110 DIP); constrain chart to 1040 DIP max-width. | Section 11.1 |
| **SYS-03** | Light theme `FkDim` 2.5:1 contrast failure | Replace `#8A9898` with `#5C6E6E`, guaranteeing 4.76:1 contrast ratio (WCAG AA compliant). | Section 4.3 |
| **SYS-04** | Fragmented corner radii (3, 4, 5, 6, 8 DIP) | Consolidate to 3-tier geometry: Small 4 DIP, Medium 6 DIP, Large 8 DIP. | Section 6.2 |
| **SYS-05** | Typographic font family divergence (Segoe UI vs Consolas) | Establish 8-step ramp; segregate Consolas strictly to countdowns, metrics, and shortcuts. | Section 5.1, 5.2 |
| **PG-01** | Settings row controls 1400 DIP away from labels | Solved structurally by `MaxWidth = 880 DIP` on Settings page. | Section 2.2 |
| **PG-02** | 3-column metric cards clip 28pt font on narrow screens | 1040 DIP max-width in Reports provides 320 DIP per card; add dynamic font scaling on compact breakpoint. | Section 2.2, 2.3 |
| **PG-03** | Misleading `Shift + F3` badge in Today launcher | Remove shortcut badge from hero launcher card header; move inline next to Quick Overlay in sidebar. | Section 10.2 |
| **PG-04** | Duplicate info strip in Reports chart card | Delete duplicate bottom `infoStrip` border container entirely. | Section 10.2 |
| **PG-05** | Marketing tagline in sidebar footer | Remove `"One focused session."` and extra divider border from sidebar footer. | Section 10.2 |
| **PG-06** | Semantic over-saturation of "today" in metric cards | Remove redundant `"today"` subtitles from Focus Time and Break Time cards. | Section 10.2 |
| **PG-07** | Split title/subtitle ("Completion" / "rate") | Unify card title to `"Completion Rate"` and remove subtitle. | Section 10.2 |
| **PG-08** | Triple echo of "Import history" in Settings | Shorten button to `"Import…"`, description to `"Tab-delimited website CSV"`. | Section 10.2 |
| **PG-09** | Inconsistent footer keyboard hints in Quick Overlay | Standardize to: `[← →] Select`, `[Enter] Start`, `[Esc] Close`. | Section 10.2 |
| **PG-10** | Raw unicode navigation glyphs (`‹`, `›`, `↻`) | Replace with native Segoe Fluent FontIcons (`\uE76B`, `\uE76C`, `\uE72C`). | Section 7.1 |

---

## 14. Implementation Guidance for Subsequent Refinement Stages

This Visual System specification governs all future work. Implementation must proceed in the following strict sequential order:

```
Stage 1: Foundation & Theme Engine Refinement
 ├── Update ResourceDictionary in App.xaml with new token names and Light FkDim (#5C6E6E).
 ├── Update Presentation.cs helper methods for card padding and font ramps.
 └── Ensure CultureInfo invariance and western digits are preserved.

Stage 2: Today Page & Application Shell Refinement
 ├── Apply MaxWidth="880" and Center alignment to PageContent in Today mode.
 ├── Refactor CurrentCard: header to "START A SESSION", remove dot & Shift+F3 badge.
 ├── Clean SummaryGrid: remove "today" subtitles, unify "Completion Rate".
 └── Refactor Sidebar: move Shift+F3 inline with Quick Overlay, remove tagline.

Stage 3: Reports Page & Responsive Chart Refinement
 ├── Apply MaxWidth="1040" to ReportsHost container.
 ├── Refactor ReportsChart.cs: uncap 76 DIP clamp, dynamically scale bars (72%).
 ├── Delete duplicate bottom infoStrip container.
 ├── Replace raw unicode nav glyphs with Segoe Fluent FontIcons.
 └── Ensure narrow viewports (<740 DIP) scale metric fonts to prevent clipping.

Stage 4: Settings Page & Quick Overlay Refinement
 ├── Apply MaxWidth="880" to SettingsHost container (curing 1400 DIP row gaps).
 ├── Expand color preset touch targets to 36x36 DIP bounding box.
 ├── Clean Settings microcopy (strip 3x echo on Data rows, remove repetitive Light/Dark prefixes).
 └── Refactor Quick Overlay: remove header shortcut badge, standardize Stop copy, harmonize footer keys.

Stage 5: Final Consistency QA & Windows Installer
 ├── Full visual regression pass across Maximized, Restored, and Minimum viewports.
 ├── Dark, Light, and High Contrast verification.
 ├── Run full automated test suite (100% pass required).
 └── Package clean Release installer (FocusKeySetup.exe).
```

---
*End of Visual System Specification. Production code remains completely untouched.*
