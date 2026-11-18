# wgetui

A desktop GUI for [winget](https://learn.microsoft.com/windows/package-manager/) on Windows: search packages in
the winget and Microsoft Store sources, install them, see which installed packages have updates, and upgrade
them one by one or all at once. The Installed page lists everything installed (also apps winget doesn't know,
marked "Local") and can uninstall them, after a confirmation. Operations run one at a time in a queue panel
with progress and cancel.

It talks to winget through its COM API (no console parsing) and is built so other package managers
(Chocolatey, npm…) can be added later.

- The (i) button on every row opens the package details: description, license, homepage, release notes and the
  available versions, each of which can be installed.
- An update you don't want can be ignored (from the details or the row's right-click menu): that version is no
  longer counted, offered by "Upgrade all" or notified, until a newer one comes out. "Show ignored" on the
  Updates page and the "Ignored updates" section in Settings bring it back.
- English and Italian UI (follows the system language by default), light/dark theme, Mica on Windows 11.
- Runs as a normal user: installers that need administrator rights ask for permission themselves.
  Don't run wgetui as administrator: winget refuses that connection.
- Packages with agreements (for example from the Microsoft Store) show them before installing, unless you turn
  on automatic acceptance in Settings. Upgrades don't ask.
- Lives in the notification area: closing the window keeps it running there (Settings can change that). The tray
  icon shows a dot for available updates (blue), a running operation (green), an installer waiting for
  administrator permission (orange) or a failure (red), and its menu can upgrade everything or check now. It can
  start with Windows, minimized to the tray.
- Checks for updates at startup and every hour, and notifies you about new ones if you said yes on first run.
- When an installer waits on the Windows permission prompt (UAC), wgetui tells you instead of looking stuck.
- Reopens apps an upgrade closed and did not restart (on by default, in Settings).
- One window per data folder: launching it again brings the running one forward.

## Requirements

- Windows 10 1809 or later / Windows 11, x64.
- winget (App Installer from the Microsoft Store), up to date.
- To build: .NET SDK 10.0.403 or later 10.0.x, GNU Make.

## Commands

Run from the repository root (`make help` lists them):

| Command | What it does |
| --- | --- |
| `make dev` | run the app with `dotnet watch` (hot reload); stops a leftover dev instance first |
| `make stop` | stop wgetui instances started from this repo (Debug), e.g. one left in the tray |
| `make run` | run the app once (Debug) |
| `make build` | build the solution |
| `make test` | run the unit tests |
| `make format` | apply code formatting |
| `make check` | formatting check + Release build with warnings as errors + tests (must be clean before committing) |
| `make publish` | self-contained single-file `publish/wgetui.exe` (keep the two `Microsoft.Management.Deployment.*` files next to it) |
| `make smoke` | opt-in live check: read-only winget search, agreements, upgrade listing, install locations, installed packages and details; never installs, upgrades or uninstalls |
| `make icons` | regenerate `src/WgetUi.App/Assets/wgetui.ico` from the artwork in `src/WgetUi.App/Icons/IconArt.cs` |
| `make clean` | `dotnet clean` |

Settings are stored in `%APPDATA%\wgetui\settings.json`, logs in `%LOCALAPPDATA%\wgetui\logs\wgetui.log`.
`wgetui.exe --data-dir <folder>` keeps both in one folder instead (and runs as a separate instance);
`--tray` starts it hidden in the notification area. "Start with Windows" adds an entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; notifications register the name `wgetui` under
`HKCU\Software\Classes\AppUserModelId`.

See [STRUCTURE.md](STRUCTURE.md) for the code layout and [AGENTS.md](AGENTS.md) for development guidelines.
