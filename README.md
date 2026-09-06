# Focus Key

Focus Key is a small, local-first Windows focus utility.

The whole product is one interaction:

```text
Shift + F3  ->  Work (30 min) or Break (10 min)  ->  Start  ->  keep working
```

It lives quietly in the system tray, runs one session at a time, records each finished session
locally, and can tell you when a session ends. Later it also answers two questions — *what is
happening today* and *how has my focus looked* — and nothing more.

## What Focus Key is not

Focus Key is deliberately small. It is not a task manager, and it will not grow into one.

No tasks, projects, accounts, cloud sync, AI, gamification, website blocking, activity
monitoring, or keyboard and mouse monitoring. Focus Key only knows about sessions the user
explicitly started.

**Golden rule:** every feature must either make starting a focus session easier, or help the user
understand their focus sessions. If it does neither, it does not belong in Focus Key.

## Technology

The application is a true native Windows app:

- C#
- Modern .NET
- WinUI 3
- Windows App SDK
- SQLite for local persistence

It does not depend on Python, PySide6, Electron, a browser UI, a web server, Docker, or any
external backend.

## Official colors

| Role       | Value     |
| ---------- | --------- |
| Background | `#0a0d0d` |
| Work       | `#183739` |
| Break      | `#434763` |
| Main text  | `#f0f4f4` |

## Repository layout

| Branch                  | Purpose                                                       |
| ----------------------- | ------------------------------------------------------------- |
| `main`                  | Reviewed baseline. No unreviewed implementation work lands here. |
| `native/phased-rewrite` | All implementation work, one verified phase at a time.        |

## How this project is built

Focus Key is implemented strictly phase by phase. A phase is designed, implemented, compiled,
tested, smoke-verified, reviewed, documented, committed, and pushed before the next phase starts.
Nothing is implemented early because it will be needed later.

Phase records live at the repository root and contain the factual implementation and
verification evidence for each phase: [`Phase 0.md`](Phase%200.md), [`Phase 1.md`](Phase%201.md),
[`Phase 2.md`](Phase%202.md), [`Phase 3.md`](Phase%203.md), [`Phase 4.md`](Phase%204.md),
[`Phase 5.md`](Phase%205.md), and [`Phase 6.md`](Phase%206.md).

| Phase | Scope                                 |
| ----- | ------------------------------------- |
| 0     | Native foundation                     |
| 1     | Session data layer                    |
| 2     | Session engine                        |
| 3     | Recovery and application coordination |
| 4     | Windows background shell              |
| 5     | Quick overlay                         |
| 6     | Notifications and completion UX       |
| 7     | Today                                 |
| 8     | Reports                               |
| 9     | Settings                              |
| 10    | Mini timer                            |
| 11    | Reliability and edge cases            |
| 12    | Design fidelity and polish            |
| 13    | Packaging and release                 |

## Runtime data

Focus Key keeps everything on the machine it runs on, under the current user's local application
data folder:

```text
%LOCALAPPDATA%\FocusKey\
├── focus_key.db
└── logs\
    └── focus_key.log
```

Nothing is sent anywhere.
