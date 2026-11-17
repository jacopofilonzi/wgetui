# Roadmap

Ideas and agreed follow-ups that are out of scope for the current version. Not a promise of order.

## Planned

- **Tray icon.** Closing the window minimizes to the tray instead of quitting; queued operations keep
  running. The tray's "Quit" asks for confirmation when an install/upgrade is running or queued (this is
  why closing the window during an operation has no confirmation today).
- **Relaunch apps closed by an upgrade.** Some installers close the app's UI and restart only their
  service (e.g. NetBird: `netbird.exe` service comes back, `netbird-ui.exe` tray app does not, even with
  the winget CLI in silent mode). Before an upgrade, record the package's processes running in the user's
  session; after a successful upgrade, start them again, non-elevated. winget doesn't map packages to
  executables, so match processes by the package's install location. Only user-session processes, never
  services or installers; behind a setting.
- **More package managers**: Chocolatey, npm, others (see "Adding a package manager" in AGENTS.md).
- **Uninstall.**
- **Ignore a specific version.** "Ignore this update" hides that exact available version of a package
  (record: manager, source, package id, version). When a newer version than the ignored one comes out, the
  update is shown again and the record is deleted, so the ignore file never grows without bound.
- **Package details page**: description, publisher, homepage, license, available versions.

## Later

- App installer (today: self-contained single-file exe).
- CI running `make check` on push and pull request.
