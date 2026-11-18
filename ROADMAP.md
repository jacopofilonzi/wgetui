# Roadmap

Ideas and agreed follow-ups that are out of scope for the current version. Not a promise of order.

## Planned

- **More package managers**: Chocolatey, npm, others (see "Adding a package manager" in AGENTS.md).

## Later

- **In-app self-update (with CI/releases).** The app checks for a newer release, and "Update" does the rest
  silently: download the setup exe, verify it (hash from the release, signature once signed), run it with
  `/VERYSILENT` and the same scope as the current install (`/CURRENTUSER` or `/ALLUSERS`, which needs UAC),
  close and relaunch the new version. No "go to GitHub and download it" link. Needs the release workflow to
  also publish a manifest the app can read. Keep the update source behind one interface so it can be swapped later.
  UI (approved mockup: https://claude.ai/artifact/5SZinW8Wtm4cYdCU7B2Nc2): a highlighted item in the left
  sidebar, right above Settings, shown only when a newer version exists. States: available ("Update wgetui",
  version) → click downloads, with a thin progress bar (click again cancels) → ready ("Restart to update") →
  click closes, installs silently and reopens; asks first if operations are queued. Compact menu: the icon
  with an accent dot.
- **Publish to winget-pkgs** (after self-update works). CI opens the manifest PR on each release
  (wingetcreate/winget-releaser) for the setup exe (`InstallerType: inno`); signing is not required. Then the self-update flow can switch to wgetui's
  own winget by replacing the update source, without touching the UI.
