# Changelog

All notable changes to wgetui are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).
Every release needs its section here before it is tagged: the release notes on GitHub and the "What's new" popup
in the app are taken from it.

## [0.2.1] - 2026-11-18

### Fixed

- After an update from inside wgetui, the setup reopens wgetui once the new version is installed.

## [0.2.0] - 2026-11-18

### Added

- wgetui updates itself: the installed copy checks GitHub for a newer version at startup and every hour, and
  "Update wgetui" appears in the menu above Settings. Click it to download the new setup (its checksum is
  verified), then "Restart to update" installs it silently and reopens wgetui.
- Settings > About shows the running version, checks for updates on demand and can turn the automatic check off.
- A notification when a new wgetui version is available (if update notifications are on).
- Restarting for an update while operations are queued or running asks first, and cancels them.

## [0.1.0] - 2026-11-18

### Added

- Search the winget and Microsoft Store sources, filter by source and install with one click.
- Package details: description, publisher, license, homepage, release notes and every available version, each
  of which can be installed.
- Package and source agreements are shown before installing, unless automatic acceptance is turned on.
- The Updates page lists the available updates: upgrade them one by one or all at once, optionally including
  packages whose installed version is unknown.
- Ignore one update version: it is no longer counted, offered or notified until a newer one comes out.
- Automatic updates per app, with an option to update apps even while they are open.
- The Installed page lists every installed package and can uninstall them after a confirmation.
- A queue panel runs operations one at a time, with progress and cancel.
- Update checks at startup and every hour, with notifications for new updates.
- Apps closed by an upgrade are reopened afterwards.
- A notification when an installer waits for administrator permission.
- Lives in the notification area, with a tray icon that shows the current state, and can start with Windows.
- English and Italian, light and dark theme.
- Per-user setup (no administrator rights needed) that upgrades an existing install in place.
