# wgetui

[![CI](https://github.com/jacopofilonzi/wgetui/actions/workflows/ci.yml/badge.svg)](https://github.com/jacopofilonzi/wgetui/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/jacopofilonzi/wgetui?include_prereleases&sort=semver)](https://github.com/jacopofilonzi/wgetui/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A desktop GUI for [winget](https://learn.microsoft.com/windows/package-manager/) on Windows: search, install,
update and uninstall apps from the winget and Microsoft Store sources, without the command line.

![Updates page](docs/screenshots/updates.png)

It talks to winget through its COM API (no console parsing) and is built so other package managers
(Chocolatey, npm…) can be added later.

## Features

**Find and install**

- Search the winget and Microsoft Store sources, filter by source, install with one click.
- The (i) button on every row opens the package details: description, license, homepage, release notes and the
  available versions, each of which can be installed.
- Packages with agreements (for example from the Microsoft Store) show them before installing, unless you turn
  on automatic acceptance in Settings. Upgrades don't ask.

![Search results](docs/screenshots/search.png)

![Package details](docs/screenshots/details.png)

**Keep everything up to date**

- See which installed packages have updates and upgrade them one by one or all at once. Operations run one at a
  time in a queue panel, with progress and cancel.
- Checks for updates at startup and every hour, and notifies you about new ones if you said yes on first run.
- An update you don't want can be ignored (from the details or the row's right-click menu): that version is no
  longer counted, offered by "Upgrade all" or notified, until a newer one comes out. "Show ignored" on the
  Updates page and the "Ignored updates" section in Settings bring it back.
- Automatic updates per app: the round-arrows button left of (i) on the Installed page (or the details popup)
  turns them on. At the startup and hourly checks wgetui queues those updates by itself; an app that is open is
  postponed to the next check (you get one notification per version) unless you allowed updating it even while
  open, per app or for all of them in Settings. A notification sums up what was updated or failed. Settings can
  pause them all.
- Reopens apps an upgrade closed and did not restart (on by default, in Settings).
- When an installer waits on the Windows permission prompt (UAC), wgetui tells you instead of looking stuck.

**Manage what's installed**

- The Installed page lists everything installed (also apps winget doesn't know, marked "Local") and can
  uninstall them, after a confirmation.

**Stays out of the way**

- Lives in the notification area: closing the window keeps it running there (Settings can change that). The tray
  icon shows a dot for available updates (blue), a running operation (green), an installer waiting for
  administrator permission (orange) or a failure (red), and its menu can upgrade everything or check now. It can
  start with Windows, minimized to the tray.
- Runs as a normal user: installers that need administrator rights ask for permission themselves.
  Don't run wgetui as administrator: winget refuses that connection.
- English and Italian UI (follows the system language by default), light/dark theme, Mica on Windows 11.
- One window per data folder: launching it again brings the running one forward.

## Install

Download `wgetui-setup-<version>-x64.exe` from the [Releases](https://github.com/jacopofilonzi/wgetui/releases)
page and run it. It installs for the current user only, in `%LOCALAPPDATA%\Programs\wgetui`, without
administrator rights (there is no "all users" option: each user installs their own copy), and starts wgetui at
the end. Running the setup again (or a newer one) upgrades the existing install; close wgetui first, the setup
asks you to.

Silent install: `wgetui-setup-<version>-x64.exe /VERYSILENT`.

Uninstall from Settings > Apps (or the Start menu entry's "Uninstall"). It removes the program, the "Start with
Windows" entry (if it points at this install) and the notification registration; your settings and logs stay in
`%APPDATA%\wgetui` and `%LOCALAPPDATA%\wgetui\logs`, so a reinstall picks them up.

## Requirements

- Windows 10 1809 or later / Windows 11, x64.
- winget (App Installer from the Microsoft Store), up to date.
- To build: .NET SDK 10.0.403 or later 10.0.x, GNU Make; for `make installer`, Inno Setup 7.

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
| `make installer` | `make publish`, then the Inno Setup 7 installer `artifacts/wgetui-setup-<version>-x64.exe` (`ISCC=<path to ISCC.exe>` if it isn't found) |
| `make smoke` | opt-in live check: read-only winget search, agreements, upgrade listing, install locations, installed packages and details; never installs, upgrades or uninstalls |
| `make icons` | regenerate `src/WgetUi.App/Assets/wgetui.ico` from the artwork in `src/WgetUi.App/Icons/IconArt.cs` |
| `make clean` | `dotnet clean` |

Settings are stored in `%APPDATA%\wgetui\settings.json`, logs in `%LOCALAPPDATA%\wgetui\logs\wgetui.log`.
`wgetui.exe --data-dir <folder>` keeps both in one folder instead (and runs as a separate instance);
`--tray` starts it hidden in the notification area; `--force-no-tray` (development) disables the tray, so closing the
window quits. "Start with Windows" adds an entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; notifications register the name `wgetui` under
`HKCU\Software\Classes\AppUserModelId`.

See [STRUCTURE.md](STRUCTURE.md) for the code layout and [AGENTS.md](AGENTS.md) for development guidelines.

## License

[MIT](LICENSE)
