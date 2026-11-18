# Project structure

wgetui is a Windows desktop app that puts a GUI on winget: search packages, install (a specific version too), list
and apply upgrades ("upgrade all" included), ignore one update version, upgrade chosen apps automatically, list installed
packages, uninstall, and show package details. Everything above the managers is manager-agnostic: the app talks to an
`IPackageManager` interface, and winget is its first implementation (through the Windows Package Manager COM
API). Other managers (Chocolatey, npm…) plug in as new projects.

```text
repo/
├── src/
│   ├── WgetUi.Core/              net10.0 — abstractions, models, registry, operation queue, agreements policy, ignore rules
│   ├── WgetUi.Managers.WinGet/   net10.0-windows10.0.26100.0, win-x64 — winget over COM
│   └── WgetUi.App/               net10.0-windows10.0.26100.0, win-x64 — Avalonia 12 + FluentAvalonia 3 UI (wgetui.exe)
├── tests/
│   ├── WgetUi.Core.Tests/            queue (and its hooks), registry, agreements policy, uninstall/target version,
│   │                                 ignored-update rules (fake manager)
│   ├── WgetUi.Managers.WinGet.Tests/ pure mapping (packages, installed, details, uninstall) + serial executor (no COM)
│   └── WgetUi.App.Tests/             string tables (EN/IT), view models (Updates, ignore, details, Installed),
│                                     tray/shell, notifications, admin prompt, relaunch, automatic updates
│                                     (rules, open-app detection, scheduler), single instance, installer mutex
│                                     (names match installer/wgetui.iss), ICO writer (all with fakes)
├── tools/WgetUi.Smoke/           opt-in live check (`make smoke`): read-only winget calls (search, agreements,
│                                 upgrades, install locations, installed count + non-removable ones, details), prints results
├── installer/wgetui.iss          Inno Setup 7 script (`make installer`): packages publish/ as
│                                 artifacts/wgetui-setup-<version>-x64.exe (version read from the exe; per-user by
│                                 default, all users on request; AppMutex; uninstall cleanup of Run value + toast
│                                 registration, settings and logs kept)
├── tools/build-installer.ps1     `make installer`: finds ISCC.exe (ISCC=, PATH, per-user, Program Files) and compiles
├── tools/stop-dev.ps1           `make stop` (run before `make dev`/`make run`): stops Debug wgetui instances of this repo
├── tools/WgetUi.IconGen/         `make icons`: renders the artwork (App `Icons/`, linked) into Assets/wgetui.ico
├── .github/workflows/ci.yml      CI: `make check` on every push and pull request (windows-latest)
├── .github/workflows/release.yml vX.Y.Z tag (= v<Version>): `make check` + `make installer` (Inno Setup pinned by
│                                 version + SHA256), GitHub pre-release with only the setup exe; manual run: artifact
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
│                             GetDetailsAsync, UninstallAsync
├── ManagerCapabilities.cs    [Flags] Search, Install, ListUpgrades, Upgrade, Progress, Cancel,
│                             IncludeUnknownVersions, Agreements, InstallLocation, ListInstalled, Uninstall,
│                             Details, InstallVersion — the UI shows actions from these
├── ManagerAvailability.cs    result of InitializeAsync (available / error code + message)
└── PackageManagerException.cs  query failure with a stable ErrorCode
Models/
├── Package.cs                ManagerId, SourceId, Id, Name, Version (null = unknown), AvailableVersion; Key;
│                             IsRemovable (false = the OS won't uninstall it: hidden on Installed, uninstall refused)
├── PackageSource.cs          ManagerId, Id, DisplayName; LocalId = "local" (installed, offered by no source)
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
│                             never touch agreements; optional target version for installs
└── IAgreementsConfirmer.cs   shows agreements to the user (implemented by the App's modal)
Updates/
└── IgnoredUpdateFilter.cs    pure "ignore this version" rules: hide the ignored version, drop a record when another
                              version is offered or (after a complete check) the package left the list
ManagerRegistry.cs            registered managers; InitializeAsync, SearchAsync, GetUpgradesAsync, GetInstalledAsync fan
                              out in parallel; one manager failing is reported as a ManagerError next to the others' results
```

## WinGet manager (`src/WgetUi.Managers.WinGet`)

| File | Responsibility |
| --- | --- |
| `WinGetManager.cs` | `IPackageManager` over `Microsoft.Management.Deployment`: activation (`new PackageManager()` through the reg-free factory DLL), sources `winget` + `msstore` (predefined catalogs; `winget-font` and user sources are not exposed), search (`CatalogDefault` contains, 50 per source), upgrades and installed list (composite catalog, `LocalCatalogs`; packages without an exposed source are `local`), agreements (catalog metadata + source agreements), details (catalog metadata of the default install version, `AvailableVersions`, installed state; local packages: ARP publisher), install location (installed version metadata, read-only), install/upgrade (silent, any scope, progress, cancellation; a target version through `InstallOptions.PackageVersionId`), uninstall (installed catalog, `UninstallPackageAsync`, silent, any scope). `FindInstalled` falls back to scanning when the `Id` filter misses local ids |
| `WinGetSerialExecutor.cs` | one-at-a-time lock for every COM call; 2 min acquire timeout and 2 min query timeout (caller fails with `timeout`, the lock is released only when the COM call returns) |
| `WinGetMapping.cs` | pure mapping: unknown-version detection, `WinGetPackageData` → `Package` (search, upgrade, installed), `WinGetDetailsData` → `PackageDetails`, install/uninstall progress, `InstallResultStatus`/`UninstallResultStatus` → error code, agreements, license cleanup, version lookup |
| `WinGetPackageData.cs`, `WinGetDetailsData.cs` | plain snapshots of a COM `CatalogPackage` (installed list: + MSIX `PackageFamilyNames`) and of its metadata/versions |
| `INonRemovablePackages.cs`, `AppxNonRemovablePackages.cs` | MSIX families Windows won't let the user uninstall, as `Get-AppxPackage` NonRemovable: `SignatureKind == System` or `appxalluserstore!IsPackageFamilyInUninstallBlocklist` (undocumented; fallback list: App Installer, Windows Security). `WinGetMapping` matches them by `PackageFamilyNames` (correlated ids such as `Microsoft.AppInstaller`) or the family of an `MSIX\<full name>` id; the installed list marks them `IsRemovable = false`, uninstall refuses them (`not_removable`) |
| `WgetUi.Managers.WinGet.csproj` | re-exports the activation DLL and `.winmd` as content (they must sit next to the exe) |

## App (`src/WgetUi.App`)

| File | Responsibility |
| --- | --- |
| `Program.cs` | entry point: installer mutex (held by every process), single-instance check (hand over and exit), process AppUserModelID, DI container, logging, **manager registration** (one line per manager), Platform wiring |
| `App.axaml(.cs)` | FluentAvalonia theme, styles, view-model → view templates; applies language/theme, registers notifications, creates the shell (window + tray), wires second-launch and toast activation, first-run question, initializes managers then starts the hourly check |
| `Assets/wgetui.ico` | app icon (exe + window), generated by `make icons` |
| `Icons/IconArt.cs`, `IconRenderer.cs`, `IcoFile.cs` | the artwork in a 64-unit design space (tile icon, tray glyph + state dot), PNG rendering, ICO packing (PNG entries); shared with `tools/WgetUi.IconGen` |
| `app.manifest` | `asInvoker` (never elevated) |
| `Infrastructure/AppPaths.cs` | settings and log paths (`--data-dir` override), `--tray` and `--force-no-tray` flags |
| `Infrastructure/SingleInstance.cs` | one instance per user + data dir: named mutex, named pipe for "activate" from a second launch |
| `Infrastructure/InstallerMutex.cs` | fixed-name mutexes `wgetui-running` + `Global\wgetui-running` for the installer's `AppMutex` (setup/uninstall ask to close wgetui), any data dir; failures logged, never fatal. Never rename |
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
| `Services/ShellHost.cs` | window show/hide, close-to-tray, Quit, activation; explicit shutdown mode. Applies `ShellCoordinator` decisions |
| `Services/TrayIconHost.cs` | Avalonia `TrayIcon` + `NativeMenu` (Open, Upgrade all (N), Check for updates, Quit); icons rendered per (taskbar theme, state) and cached |
| `Services/QuitConfirmDialog.cs`, `NotificationsConsentDialog.cs` | quit-with-pending-operations modal; first-run "notify me?" Yes/No |
| `Services/StartWithWindowsService.cs` | "Start with Windows": HKCU Run value `wgetui` = `"<exe>" --tray [--data-dir "<dir>"]`; the Run key is the source of truth |
| `Services/RelaunchPlanner.cs`, `RelaunchAfterUpgrade.cs` | `IOperationHook`: before an upgrade or an install over an installed package (e.g. a downgrade via "Install this version"), record the user-session processes running from the install folder; after success + 5 s grace, restart the missing ones non-elevated with their arguments. Pure selection rules in the planner |
| `Platform/*.cs` | OS abstractions: `IToastNotifier`, `IRunKey`, `ITaskbarTheme`, `IProcessSource` (+ `ProcessSnapshot`, `IProcessLauncher`), `IWindowFlasher` |
| `Platform/Windows/*.cs` | Windows implementations: toasts + AUMID registration, HKCU Run key, `SystemUsesLightTheme`, process list (QueryFullProcessImageName, NtQueryInformationProcess, token elevation), `Process.Start`, `FlashWindowEx` |
| `Themes/SourceBadges.axaml` | badge colours (Light/Dark) and icons per source id (`winget`, `msstore`, `local`), with a `default` fallback |
| `Themes/AppColors.axaml` | other app colours (Light/Dark): (i) button, automatic-update toggle (off/on/forced dot), details overlay/card/footer, version tags, package tile |
| `Controls/SourceBadge.axaml(.cs)` | the source pill used on every package row |
| `ViewModels/MainWindowViewModel.cs` | navigation (Search / Updates / Installed / Settings; Installed loads on first visit), the details overlay, "manager unavailable" banner, startup |
| `ViewModels/SearchViewModel.cs` | query, source filter, results, Install, details |
| `ViewModels/UpdatesViewModel.cs` | upgrades list, unknown-version toggle, "Show ignored" toggle, Refresh, Upgrade, Upgrade all, ignore/don't ignore, details; ignored updates stay out of `Count` (badge, tray), Upgrade all and `Loaded` (notifications); drops rows of uninstalled packages |
| `ViewModels/InstalledViewModel.cs` | every installed, removable package (`GetInstalledAsync`, `IsRemovable`: like Settings > Installed apps, no toggle), name/id filter, "Only automatic updates" filter, Refresh (prunes automatic-update rules), details, automatic-update toggle per row (packages `AutoUpdateService.CanAutoUpdate` accepts), Uninstall after `IUninstallConfirmer`; a successful uninstall removes the row and its rule |
| `ViewModels/PackageRowViewModel.cs` | one list row: main action, optional details / ignore / don't-ignore / automatic-update handlers, `IsQueued`, `IsIgnored`, `IsAutoUpdate`, `IsAutoUpdateForced` |
| `ViewModels/PackageDetailsViewModel.cs` | details popup: loads `PackageDetails`, info-card `Fields`, versions (Latest / Installed / "Install this version"), primary action per page (`DetailsContext`), Ignore; the chosen action runs after closing; "Automatic updates" checkboxes (Installed/Updates, applied immediately) |
| `ViewModels/PackageDetailsLauncher.cs`, `DetailsOverlay.cs` | builds the details view model for a row; `IPackageDetailsPresenter` implemented as an in-window modal overlay |
| `ViewModels/IgnoredUpdateItemViewModel.cs` | one ignored update in Settings, with Restore |
| `ViewModels/OperationsViewModel.cs`, `OperationItemViewModel.cs` | the queue panel (right column): always present, collapsed by default, expands by itself only when an operation is queued on an empty history; collapse, pending count, cancel, clear, running flag, unseen failure (red tray dot until the panel is seen or cleared) |
| `ViewModels/SettingsViewModel.cs` | language, theme, automatic acceptance of agreements, close to tray, start with Windows, update notifications, reopen closed apps, automatic updates (global switch, update open apps too), ignored updates (Restore) |
| `ViewModels/PackageActions.cs` | Install/Upgrade/Uninstall clicks → `OperationRequester` with the auto-accept setting (and an optional target version) |
| `ViewModels/TrayViewModel.cs` | tray state (dot priority admin > failure > running > updates > none), tooltip, menu texts |
| `ViewModels/ShellCoordinator.cs` | close → hide to tray (one-time hint toast) or quit; quit confirmation and cancellation of pending operations |
| `ViewModels/UpdateCheckScheduler.cs` | hourly refresh of the Updates list (`IUpdateCheckTicker`), then `AutoUpdater`; skipped while busy or operations are pending. `RunAutomaticUpdatesAsync` also runs after the startup check |
| `ViewModels/AutoUpdater.cs` | automatic updates after a scheduled check: for pending, not ignored, not already queued updates with a rule (global switch on), postpone when the app is open and nothing forces it, otherwise queue like an Upgrade click (`UpdatesViewModel.UpgradeAutomaticallyAsync`); toasts: postponed (once per key@version, `postponedUpdates`), then a summary of succeeded / failed |
| `ViewModels/UpdateNotifier.cs` | "N new updates" toast for updates not notified or seen before (key@version set in settings); skips packages `AutoUpdater` handles |
| `ViewModels/AdminPromptWatcher.cs` | from the Installing stage, polls for consent.exe every 500 ms for 20 s: toast, taskbar flash, orange tray dot until it closes |
| `Views/*.axaml` | `MainWindow` (nav pane \| content card \| `QueuePanel`; the queue is a transparent pane outside the card, styled like the nav pane, and the card rounds both top corners; the details overlay on top), `SearchView`, `UpdatesView`, `InstalledView`, `SettingsView`, `QueuePanel`, `PackageDetailsView` (header, scrolling body, footer) |

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
    "postponedUpdates": ["winget/winget/Docker.DockerDesktop@4.99.0"]
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
  (`<package key>@<available version>`), kept only while that update is pending.
  "Start with Windows" is not in the file: it is the HKCU Run value `wgetui`.
- Outside the data dir: `HKCU\Software\Classes\AppUserModelId\wgetui` (notification name + icon) and
  `%LOCALAPPDATA%\wgetui\notification-icon.png`, rewritten at every start.
- Log: `%LOCALAPPDATA%\wgetui\logs\wgetui.log` (or `<data-dir>\logs\wgetui.log`).

### Configuration

No environment variables. Command line: `wgetui.exe [--data-dir <path>] [--tray] [--force-no-tray]`. `--force-no-tray` (development,
agent checks) disables the tray icon and close-to-tray, and overrides `--tray`. `--tray` starts hidden in the
notification area (used by the Run value). A second launch with the same data dir activates the running instance
(a `--tray` one does nothing) and exits.
