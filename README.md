# wgetui

A desktop GUI for [winget](https://learn.microsoft.com/windows/package-manager/) on Windows: search packages in
the winget and Microsoft Store sources, install them, see which installed packages have updates, and upgrade
them one by one or all at once. Operations run one at a time in a queue panel with progress and cancel.

It talks to winget through its COM API (no console parsing) and is built so other package managers
(Chocolatey, npm…) can be added later.

- English and Italian UI (follows the system language by default), light/dark theme, Mica on Windows 11.
- Runs as a normal user: installers that need administrator rights ask for permission themselves.
  Don't run wgetui as administrator: winget refuses that connection.
- Packages with agreements (for example from the Microsoft Store) show them before installing, unless you turn
  on automatic acceptance in Settings. Upgrades don't ask.

## Requirements

- Windows 10 1809 or later / Windows 11, x64.
- winget (App Installer from the Microsoft Store), up to date.
- To build: .NET SDK 10.0.403 or later 10.0.x, GNU Make.

## Commands

Run from the repository root (`make help` lists them):

| Command | What it does |
| --- | --- |
| `make dev` | run the app with `dotnet watch` (hot reload) |
| `make run` | run the app once (Debug) |
| `make build` | build the solution |
| `make test` | run the unit tests |
| `make format` | apply code formatting |
| `make check` | formatting check + Release build with warnings as errors + tests (must be clean before committing) |
| `make publish` | self-contained single-file `publish/wgetui.exe` (keep the two `Microsoft.Management.Deployment.*` files next to it) |
| `make smoke` | opt-in live check: read-only winget search, agreements and upgrade listing; never installs |
| `make clean` | `dotnet clean` |

Settings are stored in `%APPDATA%\wgetui\settings.json`, logs in `%LOCALAPPDATA%\wgetui\logs\wgetui.log`.
`wgetui.exe --data-dir <folder>` keeps both in one folder instead.

See [STRUCTURE.md](STRUCTURE.md) for the code layout and [AGENTS.md](AGENTS.md) for development guidelines.
