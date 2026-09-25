# Focus Key

One shortcut. One focused session.

Press `Shift + F3` anywhere in Windows, choose Work or Break, and start. Focus Key lives in the system tray, keeps your focus history on your PC, and doesn't require an account.

## How it works

`Shift + F3` → choose Work or Break → Start

That's the main workflow. The rest of the app is there when you want to look back at your sessions or change how Focus Key behaves.

## Features

- Quick Overlay — start a session without leaving what you're doing.
- Work and Break — start, pause, continue, or stop a session at any time.
- Today — see the current session and today's focus activity.
- Reports — review your focus across Week, Month, and Year.
- System tray — Focus Key stays available without keeping a window open.
- Settings — adjust the parts of the experience you actually use.

## Reports

Week, Month, and Year views show where your focus went, with a comparison to the previous period and simple streak and consistency stats. Reports open on the current week; older periods stay put while you look through them.

## Privacy

Focus Key is local-first. No account, no cloud, no telemetry, and no network calls of its own. It doesn't monitor your websites, apps, keystrokes, mouse, screen, or webcam — it records only the sessions you start. Your history is a single file under `%LOCALAPPDATA%\FocusKey\`, and it stays there, including after you uninstall.

## Install

1. Download `FocusKeySetup.exe` from the [latest release](https://github.com/Mohaythem/Focus-Key/releases).
2. Run it. Installation is per-user (`%LOCALAPPDATA%\Programs\Focus Key`) and needs no administrator rights.
3. Open Focus Key from the Start menu.

The installer isn't code-signed yet, so Windows SmartScreen may ask first — choose More info → Run anyway. You can uninstall from Settings → Apps whenever you like; your data is left in place.

## Requirements

- Windows 10 (build 19041) or Windows 11
- 64-bit (x64)
- Self-contained — no separate .NET runtime to install

## Keyboard

- `Shift + F3` — open the Quick Overlay
- `← / →`, `Enter`, `Esc` — choose Work/Break, start, dismiss
- `Ctrl + +` / `Ctrl + -` / `Ctrl + 0` — UI scale up, down, reset

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the Windows App SDK workloads.

```powershell
dotnet build FocusKey.slnx -c Release
dotnet test  FocusKey.slnx -c Release
```

To produce the installer, publish self-contained and compile `installer.iss` with [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
dotnet publish src\FocusKey.App\FocusKey.App.csproj -c Release -r win-x64 --self-contained -o publish
iscc installer.iss
```

## License

All rights reserved. No open-source license is applied at this time.
