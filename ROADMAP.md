# Roadmap

Ideas and agreed follow-ups that are out of scope for the current version. Not a promise of order.

## Planned

- **More package managers**: Chocolatey, npm, others (see "Adding a package manager" in AGENTS.md).
- **Uninstall.**
- **Ignore a specific version.** "Ignore this update" hides that exact available version of a package
  (record: manager, source, package id, version). When a newer version than the ignored one comes out, the
  update is shown again and the record is deleted, so the ignore file never grows without bound.
- **Package details page**: description, publisher, homepage, license, available versions.

## Later

- **Automatic updates per app.** A page listing installed apps with a checkbox each: checked apps are
  upgraded automatically when an update is found (background check on a schedule, results through the
  tray/notifications). Unchecked apps keep the manual flow.
- **MSI installer.** wgetui is distributed as an MSI that installs the contents of `publish/` (today
  `wgetui.exe` plus `Microsoft.Management.Deployment.dll` and `.winmd`, which must sit next to the exe),
  with Start menu shortcut and uninstall entry. `make publish` keeps building the runnable exe directly,
  with its required files in the same folder; the MSI is built on top of that output.
- CI running `make check` on push and pull request.
