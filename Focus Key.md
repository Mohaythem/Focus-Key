title: Focus Key  
type: product-spec  
status: planning  
platform: Windows  
version: V1  
tags:

- focus-key
    
- product
    
- windows
    
- productivity
    
- local-first
    
- v1
    
# Focus Key

> [!summary]  
> **Focus Key** هو Windows focus utility صغير وخفيف يعيش في الـSystem Tray.
> 
> ضغطة واحدة على **Shift + F3** → اختار **Work** أو **Break** → **Start** → كمل شغلك.
> 
> التطبيق يسجل الجلسات محليًا ويحوّلها بعد ذلك إلى تقارير بسيطة تساعد المستخدم يفهم نمط تركيزه، بدون مراقبة نشاطه أو الحكم عليه.

## Product Positioning

> **A tiny focus utility that lets you start a work session in seconds and understand your focus habits over time.**

### Tagline

> **One shortcut. One focused session.**

---

# 1. Product Principles

Focus Key مبني على مجموعة مبادئ أساسية:

1. **Start instantly**
    
    - بدء جلسة التركيز لازم يحصل في ثوانٍ.
        
2. **Stay out of the way**
    
    - التطبيق يعيش في الخلفية ولا يطلب انتباه المستخدم باستمرار.
        
3. **Reliable timing**
    
    - الـTimer لازم يكون دقيق حتى مع Sleep / Resume أو تأخر النظام.
        
4. **Local first**
    
    - البيانات محفوظة محليًا.
        
    - لا Account.
        
    - لا Cloud requirement.
        
5. **Descriptive, not judgmental**
    
    - Focus Key يصف البيانات.
        
    - لا يحكم على المستخدم.
        
    - لا يتحول إلى Coach.
        
6. **Minimal scope**
    
    - لا Tasks.
        
    - لا Gamification.
        
    - لا Productivity system ضخم.
        

---

# 2. Golden Rule

> [!important]  
> أي Feature جديدة لازم تجاوب على سؤال واحد:
> 
> **هل بتخلي بدء جلسة التركيز أسهل، أو بتساعد المستخدم يفهم جلساته؟**
> 
> لو الإجابة **لا**، فهي مش مكانها في Focus Key حاليًا.

---

# 3. Core User Flow

Focus Key يبدأ مع Windows ويعيش بشكل أساسي في **System Tray**.

المستخدم مش محتاج يفتح الـDashboard كل مرة.

## Start a Session

الـdefault shortcut:

`Shift + F3`

لو مفيش Session شغالة، يظهر Overlay صغير في منتصف الشاشة.

الاختيارات:

### Work

**30 minutes**

Color:

`#183739`

### Break

**10 minutes**

Color:

`#434763`

ثم:

**Start**

بعد الضغط على Start:

1. الـOverlay يختفي فورًا.
    
2. الـSession تتسجل في قاعدة البيانات.
    
3. الـTimer يبدأ في الخلفية.
    
4. المستخدم يكمل شغله.
    

---

# 4. Session Completion

مثال:

**Work — 30 minutes**

بعد انتهاء الوقت:

- صوت بسيط.
    
- Windows Notification.
    
- Message:
    

> **Work session completed.**

ولا يحدث أي شيء آخر.

Focus Key **لا يقوم بـ**:

- Auto-start Break.
    
- اقتراح Session أخرى.
    
- إجبار المستخدم على اختيار شيء.
    
- Popup مزعج.
    
- Countdown للجلسة القادمة.
    

> [!note]  
> المستخدم هو صاحب القرار بالكامل في توقيت بدء الجلسة التالية.

---

# 5. Shortcut While a Session Is Running

لو مفيش Active Session:

`Shift + F3`

يفتح:

**Work / Break**

لكن لو فيه Session شغالة بالفعل:

`Shift + F3`

يفتح Active Session Overlay.

مثال:

### Work Session

`18:42 remaining`

**Stop Session**

وبالتالي:

> مستحيل تبدأ Session فوق Session أخرى.

---

# 6. Session States

كل Session لها Status واضح.

|Status|Meaning|
|---|---|
|`Running`|الجلسة شغالة حاليًا|
|`Completed`|انتهى الوقت المخطط طبيعيًا|
|`Stopped`|المستخدم أوقف الجلسة بنفسه|
|`Interrupted`|التطبيق أو Windows أغلق أثناء الجلسة في حالة لا تعتبر Completed|

الحالات دي مهمة جدًا للـhistory والـreports.

---

# 7. System Tray

Focus Key هو في الأساس **Tray App**.

Right-click على الـTray Icon يعرض Menu صغيرة:

- Start Work
    
- Start Break
    
- Open Focus Key
    
- Settings
    
- Quiet Mode
    
- Exit
    

الضغط العادي على الـTray Icon ممكن حسب الـsetting:

- يفتح Mini Timer.
    
- أو يفتح Focus Key.
    

---

# 8. Optional Mini Timer

أثناء Session ممكن يظهر Timer صغير جدًا.

مثال:

`WORK · 24:18`

أو:

`BREAK · 07:32`

## Requirements

- Compact.
    
- Draggable.
    
- Optional Always-on-top.
    
- ممكن إخفاؤه تمامًا.
    
- بدون Controls معقدة.
    

### Default

**Off**

الهدف إن Focus Key يفضل invisible قدر الإمكان.

---

# 9. Main Application

لما المستخدم يفتح Focus Key نفسه، يظهر Dashboard هادي وبسيط.

Navigation في V1:

- **Today**
    
- **Reports**
    
- **Settings**
    

ولا نحتاج أكثر من ذلك في V1.

---

# 10. Today

الصفحة الرئيسية.

## Current Session

لو فيه Session شغالة:

### Work

`18:32 remaining`

Progress bar.

**Stop**

---

## Today Summary

مثال:

### 2h 00m

Focus Time

### 4

Completed Work Sessions

### 30m

Break Time

### 80%

Completion Rate

---

## Activity Timeline

مثال:

```text
09:00  Work   ✓
09:45  Break  ✓
10:20  Work   ✓
13:10  Work   — Stopped
```

---

# 11. Reports

الهدف مش بناء Analytics Dashboard ضخم.

الهدف إن المستخدم يفهم بياناته في ثوانٍ.

Views:

- **Daily**
    
- **Weekly**
    
- **Monthly**
    

---

# 12. Core Metrics

## Actual Focus Time

إجمالي وقت جلسات Work المكتملة.

مثال:

> **7h 30m this week**

---

## Completed Work Sessions

مثال:

> **15 sessions**

---

## Completion Rate

مثال:

- Sessions Started: `20`
    
- Sessions Completed: `17`
    

```text
Completion Rate = 85%
```

---

## Break Time

مثال:

> **2h 10m**

---

## Work / Break Balance

عرض بسيط للعلاقة بين وقت Work وBreak.

بدون:

- Rating.
    
- Score.
    
- Productivity grade.
    

---

## Best Focus Period

نستخدم الـhistory لمعرفة الفترات التي يكون فيها إكمال Work Sessions أكثر اتساقًا.

مثال:

> Most of your completed Work sessions happen between 9 AM and 12 PM.

---

## Weekly Comparison

مثال:

**This week**

`7h 30m`

**Last week**

`6h 00m`

Difference:

`+1h 30m`

بدون رسائل مثل:

- Great job!
    
- You're falling behind!
    
- You need to work more.
    

Focus Key يعرض الحقيقة فقط.

---

# 13. Insights Philosophy

Insights تكون:

**Descriptive, not judgmental.**

أمثلة جيدة:

> You completed 4 Work sessions today, totaling 2 hours.

> 5 of your 6 Work sessions this week were completed.

> Your most consistent focus period this month was between 10 AM and 1 PM.

أمثلة ممنوعة:

> You were productive today.

> You failed your target.

> You should work more.

> [!important]  
> **Focus Key مش Coach.**
> 
> هو مجرد مرآة للبيانات.

---

# 14. Settings

كل الـcustomization يكون بعيدًا عن الـquick flow الأساسي.

## Sessions

### Work Duration

Default:

`30 minutes`

### Break Duration

Default:

`10 minutes`

V1:

- Work preset واحد.
    
- Break preset واحد.
    

Future:

- Custom presets.
    

---

## Shortcut

Default:

`Shift + F3`

المستخدم يقدر يغيره.

### Requirement

لازم نعمل **Shortcut Conflict Detection**.

لو الـHotkey مستخدم:

> Shortcut is unavailable.

---

## Sounds

Options:

- Session complete sound.
    
- Volume.
    
- Disable sounds.
    

ممكن نوفر حوالي 3 أصوات بسيطة فقط.

لا نحتاج Sound Library.

---

## Notifications

Options:

- Enable notifications.
    
- Work completion notification.
    
- Break completion notification.
    

---

## Startup

Option:

**Start Focus Key with Windows**

---

## Mini Timer

Options:

- Show during Work.
    
- Show during Break.
    
- Always on top.
    
- Position.
    

---

## Quiet Mode

Quiet Mode ممكن يعطّل:

- Sounds.
    
- Optional popups.
    

مع استمرار:

- Timer.
    
- Session tracking.
    
- History recording.
    

---

# 15. Visual Identity

## Background

`#0a0d0d`

## Work

`#183739`

يستخدم في:

- Work Card.
    
- Work Progress.
    
- Work Charts.
    
- Selected Work State.
    

## Break

`#434763`

يستخدم في:

- Break Card.
    
- Break Progress.
    
- Break Charts.
    
- Selected Break State.
    

## Primary Text

`#f0f4f4`

## Secondary Text

`#909b9b`

---

# 16. Design Direction

Focus Key المفروض يحسسك إنه أقرب إلى:

- **PowerToys Utility**
    
- Small Windows system utility
    

وليس:

- Notion
    
- Todoist
    
- Habitica
    

## Visual Character

- Dark.
    
- Compact.
    
- Fast.
    
- Native-feeling.
    
- Minimal animation.
    
- Clear hierarchy.
    
- Subtle states.
    

## Avoid

- Giant gradients.
    
- Neon.
    
- Gaming UI.
    
- Excessive glow.
    
- Heavy animations.
    
- Productivity gamification.
    

---

# 17. Shortcut Overlay Design

Approximate size:

`420 × 260`

Background:

`#0a0d0d`

## Structure

```text
Focus Key

┌────────────────┐ ┌────────────────┐
│      Work      │ │     Break      │
│     30 min     │ │     10 min     │
└────────────────┘ └────────────────┘

             Start
```

Work Card:

`#183739`

Break Card:

`#434763`

الـSelection لازم يكون واضح لكن بدون Glow أو Gaming effects.

---

## Keyboard Navigation

`← / →`

Switch Work / Break.

`Enter`

Start.

`Esc`

Close.

> [!note]  
> الـentire core flow لازم يكون قابل للاستخدام بدون Mouse.

---

# 18. Privacy

Privacy واحدة من أقوى نقاط Focus Key.

Focus Key **لا يراقب**:

- التطبيقات المفتوحة.
    
- المواقع.
    
- Keyboard activity.
    
- Mouse activity.
    
- Screen.
    
- Webcam.
    
- System activity.
    

Focus Key يعرف فقط:

> المستخدم بدأ Session بإرادته في الساعة X وانتهت في الساعة Y.

ولا يوجد:

- Account required.
    
- Cloud required.
    
- Analytics server required.
    

---

# 19. Local-First Data

كل البيانات Local.

Database:

**SQLite**

## `focus_sessions`

مثال للـschema:

```text
focus_sessions
├── id
├── session_type
├── started_at
├── planned_end_at
├── ended_at
├── planned_duration_seconds
├── actual_duration_seconds
├── status
└── created_at
```

---

# 20. Settings Storage

بما إن SQLite موجود بالفعل، ممكن نخزن Settings داخله.

مثلاً:

```text
settings
├── key
└── value
```

أو جدول structured حسب implementation.

---

# 21. Reports Data Model

لا نخزن Aggregated Values مثل:

```text
total_hours_this_week
```

كحقائق مستقلة.

نخزن فقط الـSessions.

ثم نحسب منها:

- Daily totals.
    
- Weekly totals.
    
- Monthly totals.
    
- Completion rate.
    
- Best focus periods.
    
- Comparisons.
    
- Insights.
    

ده يقلل احتمالات **Data Inconsistency**.

---

# 22. Logical Architecture

```text
Focus Key
│
├── Core
│   ├── Session Manager
│   ├── Timer Engine
│   ├── Session State
│   └── Events
│
├── Storage
│   ├── SQLite
│   ├── Session Repository
│   └── Settings Repository
│
├── System
│   ├── Global Shortcut
│   ├── System Tray
│   ├── Windows Notifications
│   ├── Startup
│   └── Sound
│
├── Reports
│   ├── Daily Stats
│   ├── Weekly Stats
│   ├── Monthly Stats
│   └── Insights
│
└── UI
    ├── Shortcut Overlay
    ├── Mini Timer
    ├── Today
    ├── Reports
    └── Settings
```

> [!important]  
> **Timer Engine must not depend on the UI.**
> 
> إغلاق الـMain Window لا يجب أن يوقف الجلسة.

---

# 23. Timer Reliability

الـTimer لا يعتمد على Increment / Decrement Counter كمصدر للحقيقة.

## Wrong Approach

```text
remaining_seconds -= 1
```

كل ثانية.

المشكلة:

- Windows scheduling ممكن يتأخر.
    
- التطبيق ممكن يتجمد لحظات.
    
- الجهاز ممكن يدخل Sleep.
    
- UI refresh ممكن يتأخر.
    

---

## Correct Approach

عند بداية Session:

```text
started_at = current_time
planned_end_at = started_at + duration
```

وعند الحاجة لعرض الوقت:

```text
remaining = planned_end_at - current_time
```

وبالتالي الوقت الحقيقي يعتمد على timestamps وليس عدد ticks.

---

# 24. Sleep / Resume

مثال:

- Work Session = 30 minutes.
    
- الجهاز يدخل Sleep بعد 10 دقائق.
    
- يظل في Sleep ساعة.
    
- يرجع المستخدم.
    

بما أن `planned_end_at` عدى بالفعل:

Session تعتبر:

**Completed**

وعند Resume يظهر Notification.

> [!warning]  
> Sleep / Resume behavior محتاج Reliability Tests واضحة.

---

# 25. Crash Recovery

كل Active Session لازم تتسجل في DB **فور البداية**.

مش نستنى النهاية.

عند Startup بعد Crash أو Windows shutdown:

1. نبحث عن Session status = `Running`.
    
2. نراجع `planned_end_at`.
    
3. نحدد الحالة الصحيحة.
    

Possible outcomes:

- `Completed`
    
- `Interrupted`
    

القواعد النهائية للفصل بينهم يتم تثبيتها أثناء implementation.

---

# 26. Global Shortcut

Default:

`Shift + F3`

لازم يعمل حتى لو الـforeground app هو:

- Chrome.
    
- VS Code.
    
- Game.
    
- أي Windows application آخر.
    

طالما Windows سمح بتسجيل الـHotkey.

لو حصل Conflict:

- لا يتم استخدامه بصمت.
    
- المستخدم يعرف أن الاختصار غير متاح.
    
- يقدر يختار Shortcut آخر.
    

---

# 27. Single Instance

Focus Key لازم يكون **Single-instance application**.

لو التطبيق شغال والمستخدم فتحه مرة ثانية:

1. الـsecond instance تبعت Signal للأولى.
    
2. الأولى تفتح الـMain Window.
    
3. النسخة الثانية تقفل.
    

ده يمنع:

- Duplicate timers.
    
- Duplicate tray icons.
    
- DB conflicts.
    
- Duplicate shortcut registration.
    

---

# 28. V1 Scope

> [!success]  
> لو العناصر التالية اتعملت بشكل polished، فالمنتج يعتبر V1 كامل فعلًا.

## Core

- Work Session.
    
- Break Session.
    
- Start.
    
- Stop.
    
- Finish.
    
- Session State Machine.
    

## Windows Integration

- Global Shortcut.
    
- System Tray.
    
- Native Notifications.
    
- Startup with Windows.
    
- Single Instance.
    

## UI

- Shortcut Overlay.
    
- Today.
    
- Reports.
    
- Settings.
    
- Optional Mini Timer.
    

## Data

- SQLite.
    
- Session History.
    
- Settings.
    

## Reports

- Today.
    
- Week.
    
- Month.
    
- Focus Time.
    
- Work Sessions.
    
- Break Time.
    
- Completion Rate.
    
- Basic Charts.
    
- Simple Insights.
    

## UX

- Sounds.
    
- Quiet Mode.
    
- Keyboard Navigation.
    

---

# 29. Explicitly Out of Scope for V1

> [!danger]  
> **No scope creep.**

مش هنضيف في V1:

- Tasks.
    
- To-do lists.
    
- Projects.
    
- Accounts.
    
- Login.
    
- Cloud Sync.
    
- Mobile App.
    
- Teams.
    
- Shared Sessions.
    
- Friends.
    
- Leaderboards.
    
- Achievements.
    
- XP.
    
- Levels.
    
- Streak Pressure.
    
- AI Assistant.
    
- Website Blocking.
    
- App Blocking.
    
- Screen Monitoring.
    
- Analytics Server.
    
- Calendar Integration.
    
- Spotify.
    
- Background Music Library.
    

كل دي ممكن تتناقش بعدين، لكن Focus Key مش محتاجها علشان يكون useful.

---

# 30. Development Roadmap

## Phase 0 — Foundation

نبني Skeleton نظيف.

### Tasks

- Project Structure.
    
- Application Entry Point.
    
- Logging.
    
- Error Handling.
    
- SQLite Initialization.
    
- Repository Interfaces.
    
- Single-instance Mechanism.
    

### Milestone

> Focus Key يفتح ويقفل بشكل نظيف ومستقر.

---

## Phase 1 — Session Engine

نبني قلب المنتج.

### State Flow

```text
IDLE
  ↓
RUNNING
  ↓
COMPLETED
```

أو:

```text
RUNNING
  ↓
STOPPED
```

أو:

```text
RUNNING
  ↓
INTERRUPTED
```

### Tests

- Timer calculations.
    
- State transitions.
    
- Invalid transitions.
    
- Stop behavior.
    
- Completion behavior.
    

---

## Phase 2 — Windows Background Utility

### Features

- System Tray.
    
- Global Shortcut.
    
- Startup.
    
- Notifications.
    
- Sound.
    

### Milestone

> أقدر أشغل Focus Key، أدوس Shift + F3، أبدأ Session لمدة 30 دقيقة، وأستقبل Notification عند النهاية.

دي أول نسخة technically usable من المنتج.

---

## Phase 3 — Quick Overlay

نبني الـcore UX.

### Features

- Work.
    
- Break.
    
- Start.
    
- Active Session State.
    
- Stop.
    
- Keyboard Navigation.
    

### Animation

Minimal.

Fade بسيط كفاية.

---

## Phase 4 — Database & History

نتأكد إن كل Sessions محفوظة بشكل موثوق.

### Features

- Session History.
    
- Date Filtering.
    
- Daily Aggregation.
    
- Weekly Aggregation.
    
- Monthly Aggregation.
    

---

## Phase 5 — Main Dashboard

نبني:

- Today.
    
- Reports.
    
- Settings.
    

مع الهوية البصرية النهائية.

ونضيف Charts بسيطة فقط.

---

## Phase 6 — Insights

الـInsights تكون Rule / Statistics based.

**No AI.**

مثال:

```text
if completed_work_today == 4:
    "You completed four Work sessions today."
```

## Minimum Sample Size

لا نستنتج Pattern من بيانات قليلة.

مثلاً:

ما نقولش:

> Your best focus period is 3 PM.

بناءً على Session واحدة فقط.

لازم يكون فيه Minimum Sample Size مناسب قبل إظهار Insight.

---

## Phase 7 — Reliability

اختبارات رئيسية:

-  Shortcut while Session is running.
    
-  Closing Main Window.
    
-  Exit from Tray.
    
-  Force Kill.
    
-  Restart Windows.
    
-  Sleep / Resume.
    
-  Change System Time.
    
-  Midnight Crossing.
    
-  Session starts 11:50 PM and ends 12:20 AM.
    
-  DST / Timezone issues.
    
-  Database corruption handling.
    
-  Notification failure.
    
-  Missing audio file.
    
-  Shortcut conflict.
    
-  Multiple launch attempts.
    
-  Startup recovery with active Session.
    

---

## Phase 8 — Packaging

Output:

`FocusKeySetup.exe`

Installer responsibilities:

- Install Focus Key.
    
- Start Menu entry.
    
- Optional startup configuration.
    
- Uninstall support.
    

### No

- Docker.
    
- Server.
    
- Browser runtime as product surface.
    

Focus Key هو Windows desktop utility عادي.

---

# 31. Lightweight Target

حجم `40 MB` هدف جيد، لكنه **مش Requirement مقدس**.

Framework choice ممكن يغير الحجم.

الأهم من Installer size:

- Fast startup.
    
- Low RAM usage.
    
- Near-zero idle CPU.
    
- Reasonable installer.
    
- No unnecessary dependencies.
    
- Minimal background work.
    

## Practical Performance Goals

### Idle CPU

Approximately:

`~0%`

### Idle RAM

منخفض قدر الإمكان حسب الـframework.

### Timer

لا يعمل aggressive polling.

### Startup

لازم يكون سريع جدًا.

---

# 32. Background Performance Philosophy

> [!quote]  
> Focus Key المفروض معظم الوقت بيعمل **ولا حاجة**.

في Idle نحتاج فقط:

- Global Shortcut Listener.
    
- Tray Process.
    
- Required OS hooks.
    
- DB access عند الحاجة.
    

لا نريد:

- Continuous analytics loops.
    
- Heavy background polling.
    
- Constant DB queries.
    
- Unnecessary timers.
    

---

# 33. Product Priorities

بالترتيب:

## Priority 1

**Start Focus Session instantly**

## Priority 2

**Timer reliability**

## Priority 3

**Never get in the user's way**

## Priority 4

**Accurate history**

## Priority 5

**Useful reports**

## Priority 6

**Customization**

> [!important]  
> ممكن نبني Dashboard ممتاز، لكن لو تجربة `Shift + F3 → Start` بطيئة أو مزعجة، يبقى المنتج فشل في أهم وعد له.

---

# 34. V1 Definition of Done

Focus Key V1 يعتبر ناجح لما المستخدم يقدر:

1. يشغل التطبيق مع Windows.
    
2. يلاقيه في System Tray بدون إزعاج.
    
3. يضغط `Shift + F3`.
    
4. يختار Work أو Break بالكامل بالKeyboard.
    
5. يبدأ Session فورًا.
    
6. يقفل الـMain Window بدون ما الـTimer يتوقف.
    
7. يستقبل Notification موثوقة عند النهاية.
    
8. يوقف Session يدويًا عند الحاجة.
    
9. يلاقي History صحيحة بعد Restart.
    
10. يشوف Today / Weekly / Monthly reports مفهومة.
    
11. يعدل durations والـshortcut والـsounds والـnotifications.
    
12. يستخدم التطبيق بدون Account أو Cloud.
    
13. يضمن إن Focus Key لا يراقب نشاطه.
    

---

# 35. Product Summary

**Focus Key** برنامج Windows محلي وخفيف يعيش في الـSystem Tray.

المستخدم يضغط:

`Shift + F3`

ثم يختار:

**Work — 30 min**

أو:

**Break — 10 min**

ويبدأ Session خلال ثوانٍ.

البرنامج يكمل في الخلفية، ينبه المستخدم عند النهاية، ويسجل الجلسات محليًا.

وعند الحاجة، يفتح المستخدم Dashboard بسيط يحتوي على:

- **Today**
    
- **Reports**
    
- **Settings**
    

لفهم:

- Focus Time.
    
- Completed Sessions.
    
- Break Time.
    
- Completion Rate.
    
- Focus Patterns.
    
- Weekly / Monthly Trends.
    

بدون:

- Tasks.
    
- Accounts.
    
- Cloud.
    
- Gamification.
    
- Monitoring.
    
- AI Coach.
    
- Productivity complexity.
    

---

# 36. Final Visual Tokens

```text
Background      #0a0d0d
Work            #183739
Break           #434763
Primary Text    #f0f4f4
Secondary Text  #909b9b
```

---

> [!quote]  
> **One shortcut. One focused session.**