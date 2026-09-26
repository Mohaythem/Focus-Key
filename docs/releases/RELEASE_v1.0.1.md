# Focus Key v1.0.1

One shortcut. One focused session.

Focus Key is a small Windows utility for starting Work and Break sessions quickly and reviewing your focus history afterward.

Press `Shift + F3`, choose a session, and start.

## Highlights

- Global `Shift + F3` Quick Overlay
- Work and Break sessions
- Today overview
- Week, Month, and Year reports
- System tray integration
- Local session history
- No account or cloud service required

## Download

`FocusKeySetup.exe` — Windows 64-bit installer. Per-user, no administrator rights required.

## Install

1. Download and run `FocusKeySetup.exe`.
2. It installs to `%LOCALAPPDATA%\Programs\Focus Key` for the current user.
3. Launch Focus Key from the Start menu.

The installer isn't signed yet, so Windows SmartScreen may show a notice on first run — choose More info → Run anyway.

## Requirements

- Windows 10 (build 19041) or Windows 11
- 64-bit (x64)
- Self-contained — no separate .NET runtime needed

## Privacy

Focus Key is local-first. No account, no cloud, no telemetry, and no network calls of its own. It doesn't monitor your websites, apps, keystrokes, mouse, screen, or webcam — it records only the sessions you start. Your history stays under `%LOCALAPPDATA%\FocusKey\`, including after you uninstall.

## Known limitations

- The installer is unsigned, so Windows SmartScreen prompts on first run.
- Windows x64 only in this release — no ARM64 or 32-bit build.

## Checksum

SHA-256 of `FocusKeySetup.exe`:

```
96e87cb03a7a1de5414a5b0efd8e11b83d8b86cfa2c84615f54b149f8ab9d5e7
```
