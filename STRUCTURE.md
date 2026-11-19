# Project structure

wgetui is a Windows desktop app that puts a GUI on winget: search packages, install (a specific version too), list
and apply upgrades ("upgrade all" included), ignore one update version, upgrade chosen apps automatically, list installed
packages, uninstall, show package details, and export / import package lists (`.wgetui`, also `winget export` JSON); the installed copy also updates itself from GitHub Releases
(downloaded in the background, installed at the next start) and shows "What's new" from CHANGELOG.md.
Everything above the managers is manager-agnostic: the app talks to an `IPackageManager` interface, and winget
is its first implementation (through the Windows Package Manager COM API). Other managers (Chocolatey, npm…) plug in as new projects.

```text
repo/
├── src/
│   ├── WgetUi.Core/              net10.0 — abstractions, models, registry, operation queue, agreements policy, ignore rules,
│   │                             package-list format
│   ├── WgetUi.Managers.WinGet/   net10.0-windows10.0.26100.0, win-x64 — winget over COM
│   └── WgetUi.App/               net10.0-windows10.0.26100.0, win-x64 — Avalonia 12 + FluentAvalonia 3 UI (wgetui.exe)
├── tests/
│   ├── WgetUi.Core.Tests/            queue (and its hooks), registry, agreements policy, uninstall/target version
│   │                                 (also per package), ignored-update rules (fake manager), package-list read/write
│   │                                 (Fixtures/: hand-made .wgetui + anonymised winget export)
│   ├── WgetUi.Managers.WinGet.Tests/ pure mapping (packages, installed, details, offers, uninstall) + serial executor (no COM)
│   └── WgetUi.App.Tests/             string tables (EN/IT), view models (Updates, ignore, details, Installed),
│                                     tray/shell, notifications, admin prompt, relaunch, automatic updates
│                                     (rules, open-app detection, scheduler), single instance, installer mutex
│                                     (names match installer/wgetui.iss), ICO writer, self-update (GitHub
│                                     releases parsing against Fixtures/github-releases.json, downloader on a
│                                     scripted HTTP handler, view-model state machine, install at startup),
│                                     changelog parser + "What's new" selection, export / import popups (row
│                                     states per mode, what gets queued), list download (fake HTTP handler),
│                                     --import + single-instance message, installer file association (all with fakes)
├── tools/WgetUi.Smoke/           opt-in live check (`make smoke`): read-only winget calls (search, agreements,
│                                 upgrades, install locations, installed count + non-removable ones, details, find by id), prints results
├── installer/wgetui.iss          Inno Setup 7 script (`make installer`): packages publish/ as
│                                 artifacts/wgetui-setup-<version>-x64.exe (version read from the exe; per-user
│                                 only, no UAC; AppMutex; uninstall cleanup of Run value + toast
│                                 registration, settings and logs kept; per-user `.wgetui` association
│                                 (`wgetui.PackageList`, `--import "%1"`, removed on uninstall); `/UPDATE` (self-update): waits up
│                                 to 30 s for wgetui to quit, then reopens it with `--updated`, plus
│                                 `--tray` when `/TRAY` was given too)
├── docs/screenshots/             README screenshots (light/dark split diagonally; Updates list is demo data)
├── docs/social-preview.png       GitHub social preview (1280×640), uploaded by hand in the repo settings
├── tools/build-installer.ps1     `make installer`: finds ISCC.exe (ISCC=, per-user, Program Files, PATH) and compiles
├── tools/changelog-section.ps1   one version's CHANGELOG.md section (fails if missing/empty), for the release notes
├── tools/stop-dev.ps1           `make stop` (run before `make dev`/`make run`): stops Debug wgetui instances of this repo
├── tools/WgetUi.IconGen/         `make icons`: renders the artwork (App `Icons/`, linked) into Assets/wgetui.ico
├── .github/workflows/ci.yml      CI: `make check` on every push and pull request (windows-latest)
├── .github/workflows/release.yml vX.Y.Z tag (= v<Version>, CHANGELOG.md section required): `make check` +
│                                 `make installer` (Inno Setup pinned by version + SHA256), normal GitHub release
│                                 marked Latest with only the setup exe; notes = the CHANGELOG section, then
│                                 "Installer:" and "SHA256: `<hex>`" (the self-update reads both); manual run: artifact
├── CHANGELOG.md                  release notes (Keep a Changelog, English): GitHub release body and, embedded in the
│                                 exe, the "What's new" popup; every version needs a section before tagging
├── WgetUi.slnx                   solution
├── Directory.Build.props         shared settings: nullable, warnings as errors, analyzers, <Version>
├── global.json                   SDK 10.0.403, test runner = Microsoft.Testing.Platform
├── .editorconfig                 code style (checked by `dotnet format` in `make check`)
├── ROADMAP.md                    agreed follow-ups and ideas out of the current scope
├── RESUME.md                     hand-off snapshot for a new conversation, written only on request (local, gitignored)
└── Makefile                      dev commands (`make help`)
```

## Core (`src/WgetUi.Core`)

```text
Managers/
├── IPackageManager.cs        Id, DisplayName, Capabilities, Sources, Availability, InitializeAsync, SearchAsync,
│                             GetUpgradesAsync(includeUnknown), GetAgreementsAsync, InstallAsync/UpgradeAsync
│                             (package, OperationOptions, IProgress<OperationProgress>, ct);
│                             optional (defaults: null / not_supported): GetInstallLocationAsync, GetInstalledAsync,
│                             GetDetailsAsync, UninstallAsync, FindInSourceAsync (exact id in the package's source → PackageOffer or null)
├── ManagerCapabilities.cs    [Flags] Search, Install, ListUpgrades, Upgrade, Progress, Cancel,
│                             IncludeUnknownVersions, Agreements, InstallLocation, ListInstalled, Uninstall,
│                             Details, InstallVersion, FindById — the UI shows actions from these
├── ManagerAvailability.cs    result of InitializeAsync (available / error code + message)
└── PackageManagerException.cs  query failure with a stable ErrorCode
Models/
├── Package.cs                ManagerId, SourceId, Id, Name, Version (null = unknown), AvailableVersion; Key;
│                             IsRemovable (false = the OS won't uninstall it: hidden on Installed, uninstall refused)
├── PackageSource.cs          ManagerId, Id, DisplayName; LocalId = "local" (installed, offered by no source)
├── PackageOffer.cs           what a source offers for one id: Package (catalog name, Version = latest or null) + Versions
├── PackageDetails.cs         description, publisher, homepage, license (+link), release notes, installed/available
│                             version, Versions (newest first); WebLink keeps http(s) links only
├── IgnoredUpdate.cs          ManagerId, SourceId, PackageId, Version, Name (display only); Matches(package)
├── OperationProgress.cs      Stage (Preparing/Downloading/Installing/Finalizing/Uninstalling) + Percent (null = indeterminate)
├── OperationResult.cs        Succeeded, ErrorCode, Message (English, logs only), RebootRequired
├── OperationOptions.cs       AcceptPackageAgreements, AcceptSourceAgreements (decided by the user, not managers),
│                             TargetVersion (installs only: this exact version)
├── AutoUpdateRule.cs         ManagerId, SourceId, PackageId, Force, Name (display only); IsFor(package)
├── Agreement.cs / PackageAgreements.cs   agreements of a package and of its source (+ license, informational)
└── ErrorCodes.cs             stable codes; the UI maps each to Error_<code> strings
Operations/
├── OperationQueue.cs         single sequential queue (Channel + one worker) of Install/Upgrade/Uninstall (each needs
│                             its capability; a TargetVersion also InstallVersion; an Uninstall of a package that is not
│                             IsRemovable fails with not_removable): Enqueue/EnqueueMany (dedupes active
│                             duplicates), Cancel (queued → skipped; running → token, if the manager supports it),
│                             ClearFinished, OperationAdded/OperationChanged events (raised off the UI thread)
├── QueuedOperation.cs        state machine Queued → Running → Succeeded/Failed/Cancelled, progress, Completion task
├── IOperationHook.cs         BeforeAsync / After around each operation (App: relaunch); failures are logged only
├── OperationRequester.cs     click → queued operations; the agreements policy (see AGENTS.md invariants); uninstalls
│                             never touch agreements; optional target version for installs, one for all or one per
│                             package (OperationRequest; imported pinned lists): still one agreements modal
└── IAgreementsConfirmer.cs   shows agreements to the user (implemented by the App's modal)
PackageLists/
├── PackageList.cs            PackageListEntry (manager, source, id, name, pinned version), PackageList, format,
│                             PackageListException (stable ErrorCode)
└── PackageListFile.cs        pure reader/writer of `.wgetui` (format below) + reader of `winget export` JSON; 1 MB cap
Updates/
└── IgnoredUpdateFilter.cs    pure "ignore this version" rules: hide the ignored version, drop a record when another
                              version is offered or (after a complete check) the package left the list
ManagerRegistry.cs            registered managers; InitializeAsync, SearchAsync, GetUpgradesAsync, GetInstalledAsync fan
                              out in parallel; one manager failing is reported as a ManagerError next to the others' results
```

## WinGet manager (`src/WgetUi.Managers.WinGet`)

| File | Responsibility |
| --- | --- |
| `WinGetManager.cs` | `IPackageManager` over `Microsoft.Management.Deployment`: activation (`new PackageManager()` through the reg-free factory DLL), sources `winget` + `msstore` (predefined catalogs; `winget-font` and user sources are not exposed), search (`CatalogDefault` contains, 50 per source), upgrades and installed list (composite catalog, `LocalCatalogs`; packages without an exposed source are `local`), agreements (catalog metadata + source agreements), details (catalog metadata of the default install version, `AvailableVersions`, installed state; local packages: ARP publisher), install location (installed version metadata, read-only), install/upgrade (silent, any scope, progress, cancellation; a target version through `InstallOptions.PackageVersionId`), uninstall (installed catalog, `UninstallPackageAsync`, silent, any scope), find by id (one exact-id find in the package's source, no installed-catalog scan: import resolution). `FindInstalled` falls back to scanning when the `Id` filter misses local ids |
| `WinGetSerialExecutor.cs` | one-at-a-time lock for every COM call; 2 min acquire timeout and 2 min query timeout (caller fails with `timeout`, the lock is released only when the COM call returns) |
| `WinGetMapping.cs` | pure mapping: unknown-version detection, `WinGetPackageData` → `Package` (search, upgrade, installed), `WinGetDetailsData` → `PackageDetails`, found package → `PackageOffer`, install/uninstall progress, `InstallResultStatus`/`UninstallResultStatus` → error code, agreements, license cleanup, version lookup |
| `WinGetPackageData.cs`, `WinGetDetailsData.cs` | plain snapshots of a COM `CatalogPackage` (installed list: + MSIX `PackageFamilyNames`) and of its metadata/versions |
| `INonRemovablePackages.cs`, `AppxNonRemovablePackages.cs` | MSIX families Windows won't let the user uninstall, as `Get-AppxPackage` NonRemovable: `SignatureKind == System` or `appxalluserstore!IsPackageFamilyInUninstallBlocklist` (undocumented; fallback list: App Installer, Windows Security). `WinGetMapping` matches them by `PackageFamilyNames` (correlated ids such as `Microsoft.AppInstaller`) or the family of an `MSIX\<full name>` id; the installed list marks them `IsRemovable = false`, uninstall refuses them (`not_removable`) |
| `WgetUi.Managers.WinGet.csproj` | re-exports the activation DLL and `.winmd` as content (they must sit next to the exe) |

## App (`src/WgetUi.App`)

| File | Responsibility |
| --- | --- |
| `Program.cs` | entry point: installer mutex (held by every process), single-instance check (hand over and exit), process AppUserModelID, DI container, logging, **manager registration** (one line per manager), Platform wiring; then `StartupUpdateInstaller` (a pending update starts its setup and the process exits before any UI; skipped for an `--import` start, so the file is never lost) |
| `App.axaml(.cs)` | FluentAvalonia theme, styles, view-model → view templates; applies language/theme, registers notifications, records the run for "What's new" (shown when the window is first shown), creates the shell (window + tray), wires second-launch and toast activation, first-run question, initializes managers then starts the hourly check |
| `Assets/wgetui.ico` | app icon (exe + window), generated by `make icons` |
| `Icons/IconArt.cs`, `IconRenderer.cs`, `IcoFile.cs` | the artwork in a 64-unit design space (tile icon, tray glyph + state dot), PNG rendering, ICO packing (PNG entries); shared with `tools/WgetUi.IconGen` |
| `app.manifest` | `asInvoker` (never elevated) |
| `Infrastructure/AppPaths.cs` | settings, log and self-update download paths (`--data-dir` override), `--tray`, `--force-no-tray` and `--updated` flags, `--import <path>` (never starts in the tray) |
| `Infrastructure/AppVersion.cs` | running version as x.y.z (informational version, "+commit" dropped) |
| `Infrastructure/SingleInstance.cs` | one instance per user + data dir: named mutex, named pipe for a second launch's `InstanceMessage`: first line "activate"/"tray", then optional `import:<path>` lines (older/newer versions read what they know) |
| `Infrastructure/InstallerMutex.cs` | fixed-name session-local mutex `wgetui-running` for the installer's `AppMutex` (setup/uninstall ask to close wgetui), any data dir; failures logged, never fatal. Never rename |
| `Infrastructure/AppSettings.cs`, `SettingsStore.cs`, `SettingsService.cs` | settings model, JSON load/atomic save, current settings |
| `Infrastructure/FileLoggerProvider.cs` | append-only log file (rolled to `.old` past 5 MB) |
| `Localization/Loc.cs`, `TrExtension.cs` | resx lookup, live language switch, `{l:Tr Key}` markup extension, error-code → text |
| `Resources/Strings.resx`, `Strings.it.resx` | all UI strings, English (neutral) and Italian |
| `Services/AgreementsDialog.cs` | `IAgreementsConfirmer`: one modal with every agreement as a link (text in an expander when there is no link) |
| `Services/UninstallConfirmDialog.cs` | `IUninstallConfirmer`: the "Uninstall X?" modal |
| `Services/IgnoredUpdatesService.cs` | ignored-update records in settings.json (`ignoredUpdates`): Ignore, Restore, Apply (Core rules + pruning), `Changed` |
| `Services/AutoUpdateService.cs` | per-app automatic-update rules in settings.json (`autoUpdates`) + the global switches: Set/Toggle/Remove, `CanAutoUpdate` (Upgrade capability, not `local`), `WillHandle`, `PruneInstalled` (per manager that listed its packages), `Changed` |
| `Services/OpenAppDetector.cs` | "is the app open?": install folder from the manager (read-only) + session processes, with `RelaunchPlanner`'s rules; unknown = not open |
| `Services/ThemeService.cs`, `IUiDispatcher.cs`, `AvaloniaUiDispatcher.cs` | theme variant, UI-thread marshalling |
| `Services/IFilePicker.cs`, `AvaloniaFilePicker.cs` | system save/open dialogs for package lists (main window's StorageProvider; `.wgetui`, and `.json` when opening) |
| `Services/PackageListDownloader.cs` | `IPackageListDownloader` / `HttpPackageListDownloader`: GET of an http(s) list with the shared HttpClient, 30 s, 1 MB cap on the stream; `TryParseUrl`, `Label` (host/file) |
| `Services/ShellHost.cs` | window show/hide, close-to-tray, Quit (`QuitNow` without confirmation, for the self-update restart), activation; explicit shutdown mode. Applies `ShellCoordinator` decisions |
| `Services/AppUpdates/IAppUpdateSource.cs`, `AppRelease.cs`, `AppUpdateException.cs` | where newer wgetui versions come from (swappable: one line in `Program.cs`), an installable release (version, tag, setup asset, https URL, SHA-256), failures with `app_update_*` codes |
| `Services/AppUpdates/GitHubReleaseSource.cs`, `GitHubReleaseParser.cs` | `GET api.github.com/repos/jacopofilonzi/wgetui/releases?per_page=30` (User-Agent `wgetui/<version>`, no auth, 30 s; 403 without quota / 429 → `app_update_rate_limited`). Pure selection: not draft (pre-releases count), tag `vX.Y.Z`, uploaded asset `wgetui-setup-X.Y.Z-x64.exe`, SHA-256 from the asset `digest` or the notes' "SHA256: `<hex>`" line (none → skipped), highest version wins |
| `Services/AppUpdates/AppUpdateDownloader.cs`, `AppUpdateHttp.cs`, `AppUpdateOptions.cs` | streams the setup to `<updates>\<asset>.partial` while hashing, renames it only when the SHA-256 matches; `Verify` before running; `DeleteOldSetups` at startup (`wgetui-setup-*` only, the pending one kept). One shared HttpClient. Options: version, exe folder (`IsInstalledCopy`: equals the install location, normalized, case-insensitive), download folder |
| `Services/AppUpdates/SetupCommand.cs` | setup arguments: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE [/TRAY] /LOG="<updates>\setup.log"` |
| `Services/AppUpdates/StartupUpdateInstaller.cs` | install at startup (from `Program.cs`, primary instance only): installed copy, `checkAppUpdates` on, `pendingAppUpdate` newer than the running version, file still matching its SHA-256, version not in `appUpdateAutoInstallAttempted` (recorded before the start: loop protection) → setup started (`/TRAY` for a `--tray` start), process exits. Running version ≥ pending → both records cleared. `ReadPending`/`ToRecord` convert the record |
| `Services/Changelog/ChangelogParser.cs` | pure CHANGELOG.md reading: `## [x.y.z] - date` versions (others such as Unreleased skipped), `###` sections (any name), bullets with continuation lines, inline markdown to plain text; newest first |
| `Services/Changelog/IChangelogSource.cs`, `WhatsNewSelection.cs` | the embedded CHANGELOG.md (`EmbeddedChangelogSource`, resource `wgetui.CHANGELOG.md`); pure choice of the notes: versions in (last run, current], none without a last run unless `--updated` (then the current one); Settings: all up to the current |
| `Services/UpdateRestartDialog.cs` | `IUpdateRestartConfirmer`: "Restart to update?" when operations are pending |
| `Services/TrayIconHost.cs` | Avalonia `TrayIcon` + `NativeMenu` (Open, Upgrade all (N), Check for updates, Quit); icons rendered per (taskbar theme, state) and cached |
| `Services/QuitConfirmDialog.cs`, `NotificationsConsentDialog.cs` | quit-with-pending-operations modal; first-run "notify me?" Yes/No |
| `Services/StartWithWindowsService.cs` | "Start with Windows": HKCU Run value `wgetui` = `"<exe>" --tray [--data-dir "<dir>"]`; the Run key is the source of truth |
| `Services/RelaunchPlanner.cs`, `RelaunchAfterUpgrade.cs` | `IOperationHook`: before an upgrade or an install over an installed package (e.g. a downgrade via "Install this version"), record the user-session processes running from the install folder; after success + 5 s grace, restart the missing ones non-elevated with their arguments. Pure selection rules in the planner |
| `Platform/*.cs` | OS abstractions: `IToastNotifier`, `IRunKey`, `ITaskbarTheme`, `IProcessSource` (+ `ProcessSnapshot`, `IProcessLauncher`), `IWindowFlasher`, `IAppInstallInfo` |
| `Platform/Windows/*.cs` | Windows implementations: toasts + AUMID registration, HKCU Run key, `SystemUsesLightTheme`, process list (QueryFullProcessImageName, NtQueryInformationProcess, token elevation), `Process.Start`, `FlashWindowEx`, install location (read-only: `InstallLocation` of `HKCU\…\Uninstall\{AppId}_is1`) |
| `Themes/SourceBadges.axaml` | badge colours (Light/Dark) and icons per source id (`winget`, `msstore`, `local`), with a `default` fallback |
| `Themes/AppColors.axaml` | other app colours (Light/Dark): (i) button, automatic-update toggle (off/on/forced dot), details overlay/card/footer, version tags, package tile, import status pills (`ImportStatus.*`) |
| `Themes/AppUpdateItem.axaml` | Light/Dark `NavigationViewItem*` brushes merged only into the "Update wgetui" sidebar item (highlight) |
| `Controls/SourceBadge.axaml(.cs)` | the source pill used on every package row |
| `ViewModels/MainWindowViewModel.cs` | navigation (Search / Updates / Installed / Settings; Installed loads on first visit), the details and What's new overlays, "manager unavailable" banner, startup, `AppUpdate` |
| `ViewModels/AppUpdateViewModel.cs` | self-update (installed copy only): at startup restores `pendingAppUpdate` (Ready, or Failed `app_update_install_failed` when already attempted) and clears other setups; checks (startup, hourly via `IAppUpdateChecker` when `checkAppUpdates`, manual `CheckCommand` always). Automatic checks download right away in the background (failure → Available quietly, retried next check); the manual one only offers. State None → Available → Downloading (click cancels) → Ready (record saved) → setup started with `SetupCommand` (attempt recorded) then `QuitRequested`; Failed (error, click retries download or launch). Pending operations: `IUpdateRestartConfirmer`, then cancelled. "wgetui X is ready" toast once per version after a background download (`appUpdateNotifiedVersion`, only with update notifications on) |
| `ViewModels/WhatsNewLauncher.cs`, `WhatsNewOverlay.cs`, `WhatsNewViewModel.cs` | "What's new": `RecordStartup` picks the notes and saves `lastRunVersion` before any window, `ShowPending` on the first window show, `ShowAll` from Settings > About; the overlay (like the details one) and its content (title, versions, link to the GitHub release of the current version) |
| `ViewModels/SearchViewModel.cs` | query, source filter, results, Install, details |
| `ViewModels/UpdatesViewModel.cs` | upgrades list, unknown-version toggle, "Show ignored" toggle, Refresh, Upgrade, Upgrade all, ignore/don't ignore, details; ignored updates stay out of `Count` (badge, tray), Upgrade all and `Loaded` (notifications); drops rows of uninstalled packages |
| `ViewModels/ListPopupViewModel.cs`, `PackageListOverlay.cs`, `PackageListLauncher.cs` | package-list popups: base (close, chosen action run after closing), the overlay (one at a time, like the details one), and the launcher (Export, Import from file / URL, `--import` at startup and from a second launch; reading a list is read-only) |
| `ViewModels/ExportViewModel.cs` | "Export packages": installed packages a source offers (never `local`), all selected, filter, tri-state Select all, Save → save dialog → `.wgetui` with the installed versions |
| `ViewModels/ImportViewModel.cs`, `ImportRowViewModel.cs` | "Import packages": resolution (one `GetInstalledAsync`, then `FindInSourceAsync` per package, `GetDetailsAsync` fallback; per manager one at a time, managers in parallel), Latest / Pinned versions mode, row states (To install, Installed x.y, Not found, Source unavailable, Version not available) and checkability per mode, "Install selected" (after closing: installs through `PackageActions` with one agreements modal, then upgrades; declining queues nothing) |
| `ViewModels/ImportUrlViewModel.cs` | "Import from URL": address (http/https), download, errors stay in the popup, success opens the import popup |
| `ViewModels/InstalledViewModel.cs` | every installed, removable package (`GetInstalledAsync`, `IsRemovable`: like Settings > Installed apps, no toggle), name/id filter, "Only automatic updates" filter, Refresh (prunes automatic-update rules), details, automatic-update toggle per row (packages `AutoUpdateService.CanAutoUpdate` accepts), Uninstall after `IUninstallConfirmer`; a successful uninstall removes the row and its rule; Export… and Import… (From file… / From URL…) through `PackageListLauncher` |
| `ViewModels/PackageRowViewModel.cs` | one list row: main action, optional details / ignore / don't-ignore / automatic-update handlers, `IsQueued`, `IsIgnored`, `IsAutoUpdate`, `IsAutoUpdateForced` |
| `ViewModels/PackageDetailsViewModel.cs` | details popup: loads `PackageDetails`, info-card `Fields`, versions (Latest / Installed / "Install this version"), primary action per page (`DetailsContext`), Ignore; the chosen action runs after closing; "Automatic updates" checkboxes (Installed/Updates, applied immediately) |
| `ViewModels/PackageDetailsLauncher.cs`, `DetailsOverlay.cs` | builds the details view model for a row; `IPackageDetailsPresenter` implemented as an in-window modal overlay |
| `ViewModels/IgnoredUpdateItemViewModel.cs` | one ignored update in Settings, with Restore |
| `ViewModels/OperationsViewModel.cs`, `OperationItemViewModel.cs` | the queue panel (right column): always present, collapsed by default, expands by itself only when an operation is queued on an empty history; collapse, pending count, cancel, clear, running flag, unseen failure (red tray dot until the panel is seen or cleared) |
| `ViewModels/SettingsViewModel.cs` | language, theme, automatic acceptance of agreements, close to tray, start with Windows, update notifications, reopen closed apps, automatic updates (global switch, update open apps too), ignored updates (Restore), About (version, "Check for updates" + result, "What's new", "Update wgetui automatically") |
| `ViewModels/PackageActions.cs` | Install/Upgrade/Uninstall clicks → `OperationRequester` with the auto-accept setting (and an optional target version, or one per package for imports) |
| `ViewModels/TrayViewModel.cs` | tray state (dot priority admin > failure > running > updates > none), tooltip, menu texts |
| `ViewModels/ShellCoordinator.cs` | close → hide to tray (one-time hint toast) or quit; quit confirmation and cancellation of pending operations |
| `ViewModels/UpdateCheckScheduler.cs` | hourly refresh of the Updates list (`IUpdateCheckTicker`), then `AutoUpdater`; skipped while busy or operations are pending. `RunAutomaticUpdatesAsync` also runs after the startup check. Every tick also runs the wgetui release check (`IAppUpdateChecker`), never skipped |
| `ViewModels/AutoUpdater.cs` | automatic updates after a scheduled check: for pending, not ignored, not already queued updates with a rule (global switch on), postpone when the app is open and nothing forces it, otherwise queue like an Upgrade click (`UpdatesViewModel.UpgradeAutomaticallyAsync`); toasts: postponed (once per key@version, `postponedUpdates`), then a summary of succeeded / failed |
| `ViewModels/UpdateNotifier.cs` | "N new updates" toast for updates not notified or seen before (key@version set in settings); skips packages `AutoUpdater` handles |
| `ViewModels/AdminPromptWatcher.cs` | from the Installing stage, polls for consent.exe every 500 ms for 20 s: toast, taskbar flash, orange tray dot until it closes |
| `Views/*.axaml` | `MainWindow` (nav pane with the "Update wgetui" footer item, `SelectsOnInvoked=False`, handled in `ItemInvoked` \| content card \| `QueuePanel`; the queue is a transparent pane outside the card, styled like the nav pane, and the card rounds both top corners; the details, What's new and package-list overlays on top), `SearchView`, `UpdatesView`, `InstalledView`, `SettingsView`, `QueuePanel`, `PackageDetailsView` (header, scrolling body, footer), `WhatsNewView` (same layout: notes per version, "View on GitHub", Close), `ExportView`, `ImportView`, `ImportUrlView` (same chrome) |

### Data and state

- No database. Packages are always read live from the managers; the operation queue lives in memory for the
  session.
- `settings.json` (`%APPDATA%\wgetui\settings.json`, or `<data-dir>\settings.json`), camelCase JSON:

  ```json
  {
    "language": "system", "theme": "system", "autoAcceptAgreements": false,
    "closeToTray": true, "trayHintShown": false, "notifyNewUpdates": null,
    "notifiedUpdates": ["winget/winget/7zip.7zip@26.06"], "relaunchClosedApps": true,
    "ignoredUpdates": [{ "managerId": "winget", "sourceId": "winget", "packageId": "7zip.7zip", "version": "26.06", "name": "7-Zip" }],
    "autoUpdates": [{ "managerId": "winget", "sourceId": "winget", "packageId": "Git.Git", "force": false, "name": "Git" }],
    "autoUpdatesEnabled": true, "autoUpdateOpenApps": false,
    "postponedUpdates": ["winget/winget/Docker.DockerDesktop@4.99.0"],
    "checkAppUpdates": true, "appUpdateNotifiedVersion": "0.3.1", "lastRunVersion": "0.3.0",
    "pendingAppUpdate": { "version": "0.3.1", "fileName": "wgetui-setup-0.3.1-x64.exe", "sha256": "<64 hex>" },
    "appUpdateAutoInstallAttempted": null
  }
  ```

  `language`: `system` | `en` | `it`; `theme`: `system` | `light` | `dark`. Unknown values fall back to `system`.
  `notifyNewUpdates`: `null` until the first-run question is answered. `notifiedUpdates`: updates already
  notified or seen (`<package key>@<available version>`), replaced by the current list after each complete check.
  `ignoredUpdates`: update versions the user ignored; a record is deleted when another version is offered or, after
  a complete check, when the package is no longer in the upgrade list (`IgnoredUpdateFilter`).
  `autoUpdates`: packages upgraded automatically (`force`: even while the app is open); a rule is deleted on uninstall
  and when its manager lists the installed packages without it. `autoUpdatesEnabled` pauses them all;
  `autoUpdateOpenApps` forces every rule. `postponedUpdates`: automatic updates already notified as postponed
  (`<package key>@<available version>`), kept only while that update is pending. `checkAppUpdates` ("Update wgetui
  automatically"): check at startup and every hour, download a newer wgetui in the background, install it at the next
  start (installed copy only; the manual check in Settings ignores it and never downloads by itself).
  `appUpdateNotifiedVersion`: the wgetui version already notified as ready, `null` before. `lastRunVersion`: the
  version that ran last ("What's new" shows the notes after it once), `null` until 0.3.0 first runs.
  `pendingAppUpdate`: a verified download not installed yet (file in the updates folder), cleared once the running
  version reaches it or the file no longer matches. `appUpdateAutoInstallAttempted`: the version whose setup was
  started last; still older at the next start → not started again by itself (Retry update), cleared with the pending record.
  "Start with Windows" is not in the file: it is the HKCU Run value `wgetui`.
- Outside the data dir: `HKCU\Software\Classes\AppUserModelId\wgetui` (notification name + icon) and
  `%LOCALAPPDATA%\wgetui\notification-icon.png`, rewritten at every start.
- Log: `%LOCALAPPDATA%\wgetui\logs\wgetui.log` (or `<data-dir>\logs\wgetui.log`).
- Package lists (`.wgetui`, written only where the user saves them; UTF-8 JSON, `PackageListFile`):

  ```json
  { "format": "wgetui-packages", "formatVersion": 1, "exportedAt": "2026-11-18T21:00:00Z", "exportedBy": "wgetui 0.4.0",
    "packages": [ { "managerId": "winget", "sourceId": "winget", "id": "7zip.7zip", "name": "7-Zip", "version": "26.00" } ] }
  ```

  Unknown fields are ignored; `formatVersion` above 1 → `list_newer_format`; `managerId`, `sourceId`, `id` required,
  `name` and `version` (the installed version at export, "pinned") optional; > 1 MB → `list_too_large`; anything else
  → `list_invalid`. The reader also takes `winget export` JSON (`Sources[].SourceDetails.Name` → source id under
  manager `winget`, `PackageIdentifier` → id and name, `Version`, `CreationDate`). Never add a field whose absence
  changes the meaning of an older file; a breaking change bumps `formatVersion`.
- File association (installer only, per user): `HKCU\Software\Classes\.wgetui` → `wgetui.PackageList`, whose
  `shell\open\command` is `"{app}\wgetui.exe" --import "%1"`; removed on uninstall. Development builds register nothing.
- Self-update downloads: `%LOCALAPPDATA%\wgetui\updates\` (or `<data-dir>\updates\`): the verified
  `wgetui-setup-X.Y.Z-x64.exe` (kept while it is `pendingAppUpdate`, otherwise deleted at the next start of the
  installed copy) and the setup's `setup.log` (kept, overwritten by the next update).

### Configuration

No environment variables. Command line: `wgetui.exe [--data-dir <path>] [--tray] [--force-no-tray] [--updated] [--import <path>]`. `--force-no-tray` (development,
agent checks) disables the tray icon and close-to-tray, and overrides `--tray`. `--tray` starts hidden in the
notification area (used by the Run value). `--updated` is passed by the setup when it reopens wgetui after a
self-update (shows "What's new" even without `lastRunVersion`); it changes nothing else. `--import <path>` (the `.wgetui` file association) opens that list in the import popup and
always shows the window, `--tray` or not. Unknown arguments are ignored. A second launch with the same data dir
activates the running instance (a `--tray` one does nothing), hands over its `--import` file if any, and exits.
