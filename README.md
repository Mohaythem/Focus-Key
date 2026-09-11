# Focus Key

Focus Key is a lightweight, local-first Windows focus utility designed to get out of your way and let you work.

The entire experience revolves around one seamless shortcut:

```text
Shift + F3  ->  Work (30 min) or Break (10 min)  ->  Start  ->  Keep Working
```

It lives quietly in the Windows system tray, runs one focus session at a time, records completed sessions locally, and provides simple, actionable focus reports without activity tracking, website blocking, or cloud accounts.

---

## Key Features

- **Instant Global Access**: Press `Shift + F3` anywhere in Windows to open the Quick Overlay, pick Work or Break, and start immediately.
- **System Tray Resident**: Runs in the background with minimal memory footprint; closing the main window minimizes to tray.
- **Customizable Timers & Themes**: Curated Work/Break color presets, custom hex selection, Light/Dark/System theme support, and configurable session durations.
- **Audio & Visual Alerts**: Session start and completion chimes, coupled with native Windows desktop notifications.
- **Today Dashboard**: At-a-glance view of today's completed sessions, total focus time, current streak, and activity timeline.
- **Focus Reports**: Detailed Daily, Weekly, and Monthly charts showing focus and break distributions over time.
- **Data Portability**: Import and export historical focus data using standard tab-delimited CSV formats (supporting both hours and minutes aggregate exports).
- **100% Local-First**: All data is stored in a local SQLite database on your machine. Zero telemetry, zero cloud accounts, zero background surveillance.

---

## System Requirements

- **Operating System**: Windows 10 (version 19041 / 20H1 or higher) or Windows 11 (x64)
- **Architecture**: x64

---

## Installation

1. Download the latest installer (`FocusKeySetup.exe`) from the [Releases](https://github.com/Mohaythem/Focus-Key/releases) page.
2. Run `FocusKeySetup.exe`. The installer performs a clean, per-user installation without requiring administrator privileges:
   ```text
   %LOCALAPPDATA%\Programs\Focus Key\
   ```
3. Launch **Focus Key** from the Start Menu, Windows Search, or the Desktop shortcut.

To uninstall, open **Windows Settings > Apps > Installed apps**, search for **Focus Key**, and select **Uninstall**.

---

## Building from Source

### Prerequisites

- Windows 10 (build 19041+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows App SDK / WinUI 3 build workloads
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (optional, only required for building the standalone setup installer)

### Build Commands

Clone the repository and build using the .NET CLI:

```powershell
# Restore dependencies
dotnet restore FocusKey.slnx

# Build in Release configuration
dotnet build FocusKey.slnx -c Release

# Run automated unit tests
dotnet test FocusKey.slnx -c Release
```

### Packaging the Installer

To produce the production installer (`release\FocusKeySetup.exe`):

```powershell
# Publish self-contained native application
dotnet publish src\FocusKey.App\FocusKey.App.csproj -c Release -r win-x64 --self-contained -o publish

# Compile Inno Setup installer script
iscc installer.iss
```

The output installer will be located in `release\FocusKeySetup.exe`.

---

## Local Data Storage

Focus Key keeps all persistent state strictly on your local machine:

```text
%LOCALAPPDATA%\FocusKey\
├── focus_key.db       # SQLite database (sessions, settings, history)
└── logs\
    └── focus_key.log  # Application runtime log
```

Setting the `FOCUSKEY_DATA_ROOT` environment variable allows redirecting this directory for testing or portable profiles.

---

## License

No open-source license is currently applied to this repository. All rights reserved by the author.