# Focus Key v1.0.0 Release Notes

Focus Key is a lightweight, local-first Windows desktop productivity timer designed for focused, distraction-free work.

---

## Highlights

### 1. Instant Quick Overlay (`Shift + F3`)
- Global hotkey opens a lightweight floating overlay anywhere in Windows.
- Select Work or Break durations and start sessions instantly.
- Live countdown timer, subtle progress bar, and draggable header.
- Multi-monitor support with position persistence across restarts.

### 2. Session Lifecycle & Controls
- Start, Pause, Continue, Stop, and Start New workflows.
- Non-destructive pausing preserves elapsed focus time accurately.
- Crash recovery and graceful shutdown protection for active sessions.

### 3. Today Dashboard
- Adaptive desktop composition inspired by Fluent Design.
- Hero surface displaying real-time session status and countdown.
- Daily Summary metric grid (Focus Time, Work Sessions, Break Time, Completion Rate).
- Chronological Activity timeline with local timestamp formatting.

### 4. Comprehensive Reports & Insights
- **Weekly View**: 7-day rolling window with focus duration bars and previous-week comparisons.
- **Monthly View**: 4 weekly bucket aggregation with balance metrics and top focus periods.
- **Yearly View**: 12-month calendar aggregation with year-over-year trends and consistency metrics.
- Streak statistics tracking consecutive productive focus days.

### 5. Configurable Settings & Customization
- **Durations**: Custom Work and Break session lengths down to the second.
- **Themes**: System default, Dark, and Light modes with curated color palettes.
- **Session Colors**: Custom accent colors for Work and Break sessions.
- **UI Scaling**: 6 discrete zoom levels (80%, 90%, 100%, 110%, 125%, 150%) with `Ctrl + Plus`/`Ctrl + Minus`/`Ctrl + 0` shortcuts.
- **Shortcuts**: Customizable global hotkeys for Quick Overlay and Main Window.
- **Time Format**: 12-hour (AM/PM) or 24-hour display preferences.

### 6. High-Fidelity Audio Alerts
- Distinct, pleasant audio cues for session starts, actions (pause/continue/stop), and completions.
- Master toggle and individual sound controls with unconditional preview auditioning.

### 7. Native Windows Integration
- System tray residence with minimize-to-tray and hide-on-close options.
- Single-instance enforcement with focus transfer.
- Optional "Start with Windows" background startup integration.
- Full keyboard navigation and Windows Narrator accessibility support.

### 8. 100% Local-First Data Privacy
- All session records, settings, and statistics stored locally in SQLite (`%LOCALAPPDATA%\FocusKey\focus_key.db`).
- Zero telemetry, zero cloud accounts, zero tracking.
- CSV import and export for data portability.

---

## Installation

Download and run `FocusKeySetup.exe` to install Focus Key for your Windows user profile (`%LOCALAPPDATA%\Programs\Focus Key`).
