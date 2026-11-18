# Roadmap

Ideas and agreed follow-ups that are out of scope for the current version. Not a promise of order.

## Planned

- **More package managers**: Chocolatey, npm, others (see "Adding a package manager" in AGENTS.md).

## Later

- **Publish to winget-pkgs.** CI opens the manifest PR on each release (wingetcreate/winget-releaser) for the
  setup exe (`InstallerType: inno`); signing is not required. The self-update can then read wgetui's own winget
  entry by registering another `IAppUpdateSource` instead of `GitHubReleaseSource` (one line in `Program.cs`),
  without touching the UI.
- **Signed setups.** Once the setup exe is signed, the self-update can check the signature too, on top of the
  SHA-256 it verifies today.
