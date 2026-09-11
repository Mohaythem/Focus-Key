# Phase 0 — Native Foundation

Status: **complete and verified**
Date: 2026-09-02
Branch: `native/phased-rewrite`
Baseline commit before this phase: `17ae9a8` ("Add Focus Key repository baseline")

---

## Objective

Create the smallest clean native foundation Focus Key can be built on, phase by phase, and
prove it actually works on this machine.

Phase 0 had to establish exactly this much and nothing more:

- a valid C#/.NET solution with clear project boundaries
- a WinUI 3 application project on the Windows App SDK
- a real automated test project
- a minimal, explicit application bootstrap
- deterministic application data paths
- minimal local-only logging
- foundational handling for otherwise unhandled failures
- the smallest SQLite infrastructure that proves database initialization works
- a minimal placeholder native window
- clean build and test workflows
- a safe Git/GitHub baseline with the work pushed to the official remote

No Focus Key product feature was implemented. No future-phase scope was imported.

---

## Implemented scope

**Solution.** `FocusKey.slnx` (the .NET 10 XML solution format) with three projects:

| Project | Path | Purpose |
| --- | --- | --- |
| `FocusKey.App` | `src/FocusKey.App` | WinUI 3 application, bootstrap, placeholder window |
| `FocusKey.Foundation` | `src/FocusKey.Foundation` | Paths, logging, SQLite bootstrap |
| `FocusKey.Foundation.Tests` | `tests/FocusKey.Foundation.Tests` | Automated tests for the foundation |

**Application paths.** `AppPaths` is the single place that knows where runtime data lives.
Nothing else composes filesystem paths.

**Logging.** `FileAppLogger` appends UTC-stamped lines to one local file. Local only: no
telemetry, no network, no analytics, no machine-data collection.

**Error handling.** Unhandled XAML exceptions, unhandled domain exceptions, and unobserved task
exceptions are recorded. A startup failure writes a fallback report and shows a native message
box instead of the process disappearing silently.

**SQLite.** A connection factory with fixed pragmas, plus a forward-only migration runner that
tracks the applied version. One migration exists and it creates schema metadata only.

**Placeholder window.** A single window showing the application name, a line confirming the
foundation is running, and the resolved runtime facts (version, data root, database, schema
version, log file).

---

## Architecture decisions

**Two source projects, not four.** The instructions allowed a core project and a data project
only if genuinely needed now. Paths, logging, and the SQLite bootstrap are all small and all
consumed by the same startup path, so they live in one `FocusKey.Foundation` library. Splitting
them today would create layers with nothing in them. The boundary that matters — UI process
versus non-UI logic — is real and is enforced: `FocusKey.Foundation` targets plain `net10.0` and
references no Windows UI types, which is also what makes it testable without a UI host.

**No dependency injection container.** Composition is three objects created in order in
`FoundationBootstrap.Run()`. A container would add indirection without removing any.

**Unpackaged for development.** `WindowsPackageType=None`, framework-dependent, running against
the machine-wide Windows App Runtime. This is the narrowest configuration that compiles and
launches: no MSIX manifest, no packaging project, no signing, no certificate. It is also
reversible — packaging can be added in Phase 13 without reworking the application. Nothing about
production installation, startup registration, or notification activation was implemented.

**Managed platform is architecture-neutral; the runtime identifier is pinned.** The application
sets `RuntimeIdentifier=win-x64`, which is what selects the native Windows App SDK binaries.
Libraries and tests stay neutral, so the whole solution builds into one output tree with no
per-platform flags on the command line. Additional architectures are a packaging concern.

**Timestamps in SQLite are ISO-8601 UTC text.** Chosen now so the first stored column type does
not have to be revisited later. No product columns exist yet.

**WAL journal mode.** Readers never block the writer and the database survives an abrupt exit.
`synchronous=NORMAL` is the usual desktop pairing, and `busy_timeout=5000` stops a briefly
locked database from failing instantly.

**Schema version lives in `PRAGMA user_version`, with an audit table beside it.** The pragma is
the authoritative version; `schema_migrations` records what was applied and when. Migrations run
inside a transaction, forward only, and initialization verifies the end state instead of assuming
it.

**Migrations take the write lock before deciding.** Each migration runs in a `BEGIN IMMEDIATE`
transaction and re-reads `user_version` inside it, so two processes starting at the same moment
cannot both apply the same migration. The loser observes the applied version and skips. (Added by
the post-phase audit — see below.)

**One canonical timestamp text for the database.** `UtcTimestamp` formats and parses
`yyyy-MM-ddTHH:mm:ss.fffffffZ`: always UTC, fixed width, sortable as ordinary text, exact to a
.NET tick. Every timestamp written to SQLite goes through it, starting with the migration audit
column.

**`FOCUSKEY_DATA_ROOT` override.** `AppPaths.Resolve()` honours this environment variable and
otherwise uses `%LOCALAPPDATA%\FocusKey`. It exists because the required runtime smoke test would
otherwise have written into a pre-existing `%LOCALAPPDATA%\FocusKey` folder left by the previous
implementation (see Known limitations). It is a verification affordance, not a product feature,
and the default path is unchanged.

---

## Toolchain

Versions were chosen from what is actually installed on this machine, not inherited from anywhere.

| Item | Version | Why this one |
| --- | --- | --- |
| .NET SDK | `10.0.400` | The only SDK installed; MSBuild `18.9.6` |
| Target framework (app) | `net10.0-windows10.0.19041.0` | Current .NET; matching desktop runtime `10.0.11` is installed |
| `TargetPlatformMinVersion` | `10.0.17763.0` | Keeps the floor at Windows 10 1809 rather than raising it without cause |
| Target framework (library, tests) | `net10.0` | No Windows UI dependency, so no Windows TFM needed |
| Windows App SDK | `Microsoft.WindowsAppSDK 2.4.0` | Latest stable, and `Microsoft.WindowsAppRuntime.2 2.4.0.0` is installed machine-wide (x64 and x86), so the unpackaged bootstrapper finds an exact match |
| SQLite | `Microsoft.Data.Sqlite 10.0.11` | Latest stable on the .NET 10 line; bundles `e_sqlite3`, no EF Core, no external native install |
| Test framework | `xunit 2.9.3` with `xunit.runner.visualstudio 3.1.4` | Mature default the SDK's own template pins |
| Test host | `Microsoft.NET.Test.Sdk 17.14.1` | Template-pinned, known good with SDK 10.0.400 |

Also present and relevant: `MicrosoftCorporationII.WinAppRuntime.Main.2 2.4.0.0` and
`MicrosoftCorporationII.WinAppRuntime.Singleton 8002.4.0.0`, which unpackaged Windows App SDK apps
need at runtime.

Not installed, and not required for this phase: a standalone Windows SDK
(`C:\Program Files (x86)\Windows Kits\10` does not exist). The managed WinUI 3 build restores its
projections from NuGet and compiles XAML fine without it, which this phase proves. MSIX packaging
and signing tooling may still need it in Phase 13; that remains unverified.

Coverage collection (`coverlet.collector`) was removed from the test project — Phase 0 has no
coverage requirement and it was an unused dependency.

---

## Files created

Solution and build configuration:

- `FocusKey.slnx`
- `Directory.Build.props`
- `Phase 0.md`

`src/FocusKey.Foundation`:

- `FocusKey.Foundation.csproj`
- `AppPaths.cs`
- `Logging/IAppLogger.cs`
- `Logging/FileAppLogger.cs`
- `Logging/NullAppLogger.cs`
- `Data/SqliteConnectionFactory.cs`
- `Data/SchemaMigration.cs`
- `Data/DatabaseBootstrapper.cs`
- `Data/DatabaseInitializationResult.cs`
- `Data/UtcTimestamp.cs`

`src/FocusKey.App`:

- `FocusKey.App.csproj`
- `app.manifest`
- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Startup/FoundationBootstrap.cs`
- `Startup/StartupContext.cs`
- `Startup/FatalError.cs`

`tests/FocusKey.Foundation.Tests`:

- `FocusKey.Foundation.Tests.csproj`
- `TempDirectory.cs`
- `AppPathsTests.cs`
- `FileAppLoggerTests.cs`
- `DatabaseBootstrapperTests.cs`
- `UtcTimestampTests.cs`

Untracked local verification artifacts (ignored by `.gitignore`, kept out of the repository on
purpose): `.smoke/run-smoke.ps1`, `.smoke/window.png`, `.smoke/appdata/`.

## Files modified

- `.gitignore` — added one line, `machine-local settings`, so machine-local assistant
  permissions can never be committed from any clone. Everything else in the baseline
  `.gitignore` was already correct for a .NET/WinUI tree and was left untouched.

## Files removed

- `tests/FocusKey.Foundation.Tests/UnitTest1.cs` — the placeholder emitted by
  `dotnet new xunit` minutes earlier, replaced by the real test files above.
- Six stale build output directories from an earlier per-project build that used a different
  MSBuild platform (`bin/x64` and `obj/x64` under each of the three projects). Generated
  artifacts only, never tracked by Git, removed so no stale executable could be mistaken for the
  verified build.

No authoritative input was touched: `Focus Key.md`, `Focus Key.zip`, and `README.md` are
byte-for-byte unchanged.

---

## Runtime data locations

Default, per user, local (never roaming), matching what `README.md` already documents:

```text
%LOCALAPPDATA%\FocusKey\
├── focus_key.db
└── logs\
    └── focus_key.log
```

On this machine that resolves to `%LOCALAPPDATA%\FocusKey`.

A fallback report is written to `logs\startup-failure.log` if startup fails before normal logging
is usable.

Setting `FOCUSKEY_DATA_ROOT` moves the whole tree to that directory instead. The smoke test used
`.smoke\appdata` for exactly this reason.

Nothing is written anywhere else. No registry keys, no roaming data, no temp files, no network
calls.

---

## Database foundation

**What exists after initialization**

- the database file itself, created on first open
- `PRAGMA user_version = 1` as the authoritative schema version
- one table, `schema_migrations (version INTEGER PRIMARY KEY, name TEXT, applied_at_utc TEXT)`,
  containing one row: `1 | schema_metadata | <ISO-8601 UTC>`
- WAL journal mode, `synchronous=NORMAL`, `busy_timeout=5000`, `foreign_keys=ON`
- verification that the end state is the expected version and that the metadata table is present
- a predictable failure if the file reports a schema newer than this build understands
- idempotent behaviour: a second initialization applies nothing and changes nothing

**What deliberately does not exist**

No `focus_sessions` table. No settings table. No session repository, interface, or persistence
abstraction of any kind. No session CRUD, no reports queries, no recovery logic, no active-session
state, no application coordination. A test asserts that the only table in a freshly initialized
database is `schema_migrations`, so this cannot drift silently.

---

## Tests

38 tests, all foundational. Nothing here tests session behaviour, because none exists.

**`AppPathsTests` (10)** — the documented layout is composed exactly; relative segments are
normalized; repeated resolution is deterministic; blank and null roots fail predictably;
`ForLocalApplicationData()` lands under `%LOCALAPPDATA%\FocusKey`; `Resolve()` prefers
`FOCUSKEY_DATA_ROOT` and falls back when it is blank; `EnsureCreated()` creates both directories
and is safe to repeat without disturbing existing content.

**`FileAppLoggerTests` (7)** — a missing log directory is created; level and message are recorded
in the expected shape; timestamps are UTC and parseable; `Error` includes full exception detail;
a second session appends instead of truncating; writing after dispose is harmless rather than
throwing during shutdown; a blank path fails predictably.

**`DatabaseBootstrapperTests` (10)** — a new database reaches the target schema version and reports
what it did; the migration row is recorded with a canonical UTC timestamp; a second initialization
applies nothing and does not duplicate rows (idempotency); a database claiming a newer schema
version is rejected with a clear message; **no product tables are created**; WAL is actually in
effect on an opened connection; two writers initializing the same file at once end at the target
version with exactly one migration applied and no duplicate audit row; missing constructor
arguments and a blank database path fail predictably.

**`UtcTimestampTests` (11)** — the canonical shape is produced exactly; offsets are normalized to
UTC so the same instant always writes the same text; round-trip is exact to the tick; formatted
values sort correctly as plain text across a midnight boundary; null, empty, date-only,
second-precision, and offset-suffixed forms are all rejected rather than half-accepted.

The reasoning behind the selection: these are the failure modes that would silently corrupt every
later phase — a path that differs between runs, a log that cannot be written, a database that
re-applies migrations or is opened by the wrong build.

---

## Verification commands

Run from `the repository root`:

```text
dotnet --info
dotnet restore FocusKey.slnx
dotnet build FocusKey.slnx -c Debug --no-restore
dotnet test FocusKey.slnx -c Debug --no-build
powershell.exe -NoProfile -File ".smoke\run-smoke.ps1"
```

Git state was inspected before anything was created:

```text
git ls-remote https://github.com/Mohaythem/Focus-Key.git
git init -b main
git remote add origin https://github.com/Mohaythem/Focus-Key.git
git fetch origin
git log --oneline --all --decorate
git ls-tree -r --long origin/main
git checkout -b native/phased-rewrite origin/native/phased-rewrite
git branch --track main origin/main
```

## Verification results

Actual output, not expected output.

**Restore**

```text
Determining projects to restore...
All projects are up-to-date for restore.
```

(The first restore of `Microsoft.WindowsAppSDK 2.4.0` took 5.2 minutes on this machine.)

**Build**

```text
FocusKey.Foundation -> src\FocusKey.Foundation\bin\Debug\net10.0\FocusKey.Foundation.dll
FocusKey.Foundation.Tests -> tests\FocusKey.Foundation.Tests\bin\Debug\net10.0\FocusKey.Foundation.Tests.dll
FocusKey.App -> src\FocusKey.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FocusKey.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:09.64
```

**Tests**

```text
Passed!  - Failed:     0, Passed:    38, Skipped:     0, Total:    38, Duration: 126 ms
          - FocusKey.Foundation.Tests.dll (net10.0)
```

Totals: 38 total, 38 passed, 0 failed, 0 skipped.

---

## Runtime smoke test

Real launch of the built executable
`src\FocusKey.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FocusKey.exe`, twice, against
the isolated data root `.smoke\appdata` (which did not exist beforehand).

**Run 1 — first ever start**

```text
data root exists      : False        (before launch)
process id            : 9924
has exited            : False
responding            : True
main window handle    : 22219454
main window title     : 'Focus Key'
working set (MB)      : 139.2
window image          : .smoke\window.png (660 x 400)

close request sent    : True
exited within 20s     : True
exit code             : 0
```

Runtime data created by that launch:

```text
Size  LastWriteTimeUtc      FullName
1     9/2/2026 4:45:45 AM   .smoke\appdata\logs
8192  9/2/2026 4:45:53 AM   .smoke\appdata\focus_key.db
843   9/2/2026 4:45:53 AM   .smoke\appdata\logs\focus_key.log
```

Log written by that launch:

```text
04:45:45.776Z [INFO ] Focus Key 1.0.0+17ae9a886c21a8305ee9dd67708506372cf16d1c starting (process 9924).
04:45:45.778Z [INFO ] Application data root: .smoke\appdata
04:45:45.778Z [WARN ] Data root came from FOCUSKEY_DATA_ROOT.
04:45:45.778Z [INFO ] Log file: .smoke\appdata\logs\focus_key.log
04:45:45.859Z [INFO ] Applied database migration 1 (schema_metadata).
04:45:45.860Z [INFO ] Created database file '.smoke\appdata\focus_key.db'.
04:45:45.860Z [INFO ] Database ready at schema version 1.
04:45:45.861Z [INFO ] Foundation initialization complete.
04:45:45.974Z [INFO ] Placeholder window displayed.
04:45:53.025Z [INFO ] Main window closed. Focus Key shutting down.
```

**Run 2 — second start against the existing database**

```text
process id            : 18672
responding            : True
main window handle    : 5048184
main window title     : 'Focus Key'
working set (MB)      : 131.1
exit code             : 0
```

```text
04:45:53.451Z [INFO ] Focus Key 1.0.0+17ae9a88... starting (process 18672).
04:45:53.454Z [INFO ] Application data root: .smoke\appdata
04:45:53.514Z [INFO ] Database ready at schema version 1.
04:45:53.515Z [INFO ] Foundation initialization complete.
04:45:53.573Z [INFO ] Placeholder window displayed.
04:46:00.212Z [INFO ] Main window closed. Focus Key shutting down.
```

No "Applied database migration" line on the second start: initialization is idempotent in the
real application, not just in tests.

**What this establishes**

| Requirement | Evidence |
| --- | --- |
| Native app launches | Process started, `Responding = True`, exit code 0 |
| Placeholder window appears | Window handle non-zero, title `Focus Key`, captured 660×400 image, `Placeholder window displayed.` |
| Initialization completes | `Foundation initialization complete.` on both runs |
| Data directories initialize | `.smoke\appdata` and `.smoke\appdata\logs` created by the app |
| SQLite initialization succeeds | Database file created, migration 1 applied, `Database ready at schema version 1.` |
| Logging works | 843-byte log with every lifecycle line |
| Clean shutdown | `WM_CLOSE` accepted, process exited within 20 s, exit code 0, shutdown line logged |

---

## Post-phase independent audit (2026-09-02)

Phase 0 was re-audited from the repository rather than from its own report, before Phase 1 was
allowed to start. Verified independently: tracked file inventory (30 files, no build output),
`Focus Key.md` and `Focus Key.zip` unchanged with the archive still intact, no future-phase
vocabulary anywhere in tracked source (no timer, tray, hotkey, notification, overlay,
single-instance, Today, Reports, Work/Break, or session statuses), no web-prototype code, no
hard-coded machine paths, every `catch` site justified, and a fresh
restore → non-incremental build → test → runtime smoke cycle.

One defect was found and corrected inside Phase 0 scope:

**Concurrent first start could crash the migration runner.** Two processes launching
simultaneously against a brand-new database could both read `user_version = 0`, and the second
would then fail inserting a duplicate `schema_migrations.version` row. Migrations now run in a
`BEGIN IMMEDIATE` transaction and re-read `user_version` inside it; the second writer sees the
applied version and skips. `Initialize` still reports only what it applied itself, so the
distinction stays visible. Covered by `Initialize_IsSafeWhenTwoWritersStartTogether`.

Two smaller hardening changes came with it: `AppliedMigrations` is now returned as a genuinely
read-only list, and the migration audit timestamp is written through the new `UtcTimestamp`
formatter so the database has one timestamp representation. Rows written by the pre-correction
build keep their original ISO-8601 text; nothing parses that column programmatically, so no data
migration was needed.

Post-correction verification: build succeeded with 0 warnings and 0 errors, 38/38 tests passed, and
the runtime smoke test passed on a fresh data root (`Focus Key` window shown, migration 1 applied,
schema version 1, clean exit code 0 on both runs).

Single-instance behaviour, which would make concurrent starts impossible in the first place,
remains Phase 4 scope and was **not** implemented here.

---

## Known limitations

1. **Pre-existing user data at the default path needs a decision.**
   `%LOCALAPPDATA%\FocusKey` already contained `focus_key.db` (20,480 bytes,
   modified 2026-09-02 04:06) and `logs\focus_key.log` (1,494 bytes, 04:28) before this phase
   started — left by the previous implementation. It was **not read, not opened, not modified,
   and not deleted**, and the smoke test was redirected precisely so it would stay untouched. On a
   real first launch the new application will open that file and add its schema metadata to it.
   Decide before then whether that data should be archived, removed, or adopted. This is the
   user's call, not an implementation detail.
2. **No log rotation.** One file, appended forever. Fine for a foundation, not fine for a tray
   app that runs all day. A size cap belongs to a later phase.
3. **Unpackaged, x64 runtime identifier only.** No MSIX, no installer, no signing, no startup
   registration — all Phase 13. Additional architectures are one property change when needed.
4. **Output size and memory are untuned.** The Debug output tree is ~40 MB because Windows App SDK
   2.4 ships AI, ML, and WebView2 projections alongside WinUI, and the smoke runs showed a
   131–139 MB working set for a Debug build. Neither figure has been optimized, and neither is
   evidence about a Release build.
5. **No standalone Windows SDK on this machine.** Managed WinUI 3 builds and XAML compilation work
   without it, as proven here. Whether MSIX packaging and signing need it is still unverified.
6. **The placeholder window has no product design.** Default WinUI theme, default title bar, no
   Focus Key palette or typography. Visual work starts when the design phases authorize it.
7. **The smoke script is not in the repository.** It lives in `.smoke/`, which the baseline
   `.gitignore` excludes as local verification output. The exact commands are recorded above so
   the run is reproducible.
8. **Test temp directories may occasionally linger** in `%TEMP%\focus-key-tests` when SQLite
   connection pooling still holds a file handle at cleanup. Harmless, and never fails a test.

---

## Deferred work (explicitly not implemented)

Everything below was named as out of scope for Phase 0 and none of it exists in the tree:

- session domain model, Work/Break behaviour, `RUNNING`/`COMPLETED`/`STOPPED`/`INTERRUPTED`
  workflow, session state machine
- session repository interfaces, session repositories, `focus_sessions` table, session
  persistence abstractions, settings persistence
- session engine, timer engine, timestamp-based remaining-time logic, session completion
- crash recovery, recovery classification, session/application coordinator
- single-instance behaviour, inter-process activation, hidden Win32 message windows
- tray icon, tray menu, global shortcut, `Shift + F3` handling
- Quick Overlay, Active Session Overlay, Mini Timer
- notifications, completion sound, Quiet Mode
- Today, Reports, Reports calculations, Settings
- Work/Break colour customization, theme implementation, final design implementation
- startup registration, packaging, installer, release pipeline

---

## Scope confirmation

`NO FUTURE-PHASE PRODUCT FUNCTIONALITY WAS IMPLEMENTED IN PHASE 0.`








