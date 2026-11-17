# Roadmap

Ideas and agreed follow-ups that are out of scope for the current version. Not a promise of order.

## Planned

- **More package managers**: Chocolatey, npm, others (see "Adding a package manager" in AGENTS.md).

## Later

- **Automatic updates per app.** A page listing installed apps with a checkbox each: checked apps are
  upgraded automatically when an update is found (background check on a schedule, results through the
  tray/notifications). Unchecked apps keep the manual flow.
- **MSI installer.** wgetui is distributed as an MSI that installs the contents of `publish/` (today
  `wgetui.exe` plus `Microsoft.Management.Deployment.dll` and `.winmd`, which must sit next to the exe),
  with Start menu shortcut and uninstall entry. The installer lets the user choose the scope: system-wide
  (Program Files, all users, needs UAC) or per-user (`%LOCALAPPDATA%`, no admin) — WiX dual-purpose package. `make publish` keeps building the runnable exe directly,
  with its required files in the same folder; the MSI is built on top of that output.
- CI running `make check` on push and pull request.
- **In-app self-update (with CI/releases).** The app checks for a newer release, and "Update" does the rest
  silently: download the MSI, verify it (hash from the release, signature once signed), run `msiexec`
  quietly with the same scope as the current install (per-machine needs UAC), close and relaunch the new
  version. No "go to GitHub and download it" link. Needs CI publishing versioned MSIs with a manifest the app
  can read. Keep the update source behind one interface so it can be swapped later.
  UI (approved mockup: https://claude.ai/artifact/5SZinW8Wtm4cYdCU7B2Nc2): a highlighted item in the left
  sidebar, right above Settings, shown only when a newer version exists. States: available ("Update wgetui",
  version) → click downloads, with a thin progress bar (click again cancels) → ready ("Restart to update") →
  click closes, installs silently and reopens; asks first if operations are queued. Compact menu: the icon
  with an accent dot.
- **Publish to winget-pkgs** (after self-update works). CI opens the manifest PR on each release
  (wingetcreate/winget-releaser); signing is not required. Then the self-update flow can switch to wgetui's
  own winget by replacing the update source, without touching the UI.
