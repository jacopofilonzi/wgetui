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

## Maybe

- Uninstall.
- Ignore a specific update.
- Package details page.
- App installer (today: self-contained single-file exe).
- CI running `make check` on push and pull request.
- Leave the native `.pdb` files (SkiaSharp, HarfBuzz) out of the publish folder.
