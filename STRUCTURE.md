# Project structure

wgetui is a Windows desktop app that puts a GUI on winget: search packages, install, list and apply upgrades
("upgrade all" included). Everything above the managers is manager-agnostic: the app talks to an
`IPackageManager` interface, and winget is its first implementation (through the Windows Package Manager COM
API). Other managers (Chocolatey, npm…) plug in as new projects.

```text
repo/
├── src/
│   ├── WgetUi.Core/              net10.0 — abstractions, models, registry, operation queue, agreements policy
│   ├── WgetUi.Managers.WinGet/   net10.0-windows10.0.26100.0, win-x64 — winget over COM
│   └── WgetUi.App/               net10.0-windows10.0.26100.0, win-x64 — Avalonia 12 + FluentAvalonia 3 UI (wgetui.exe)
├── tests/
│   ├── WgetUi.Core.Tests/            queue (and its hooks), registry, agreements policy (fake manager)
│   ├── WgetUi.Managers.WinGet.Tests/ pure mapping + serial executor (no COM activation)
│   └── WgetUi.App.Tests/             string tables (EN/IT), view models, tray/shell, notifications, admin
│                                     prompt, relaunch, single instance, ICO writer (all with fakes)
├── tools/WgetUi.Smoke/           opt-in live check (`make smoke`): read-only winget calls, prints results
├── tools/WgetUi.IconGen/         `make icons`: renders the artwork (App `Icons/`, linked) into Assets/wgetui.ico
├── WgetUi.slnx                   solution
├── Directory.Build.props         shared settings: nullable, warnings as errors, analyzers, <Version>
├── global.json                   SDK 10.0.403, test runner = Microsoft.Testing.Platform
├── .editorconfig                 code style (checked by `dotnet format` in `make check`)
├── ROADMAP.md                    agreed follow-ups and ideas out of the current scope
└── Makefile                      dev commands (`make help`)
```

## Core (`src/WgetUi.Core`)

```text
Managers/
├── IPackageManager.cs        Id, DisplayName, Capabilities, Sources, Availability, InitializeAsync, SearchAsync,
│                             GetUpgradesAsync(includeUnknown), GetAgreementsAsync, InstallAsync/UpgradeAsync
│                             (package, OperationOptions, IProgress<OperationProgress>, ct);
│                             optional GetInstallLocationAsync (default null)
├── ManagerCapabilities.cs    [Flags] Search, Install, ListUpgrades, Upgrade, Progress, Cancel,
│                             IncludeUnknownVersions, Agreements, InstallLocation — the UI shows actions from these
├── ManagerAvailability.cs    result of InitializeAsync (available / error code + message)
└── PackageManagerException.cs  query failure with a stable ErrorCode
Models/
├── Package.cs                ManagerId, SourceId, Id, Name, Version (null = unknown), AvailableVersion; Key
├── PackageSource.cs          ManagerId, Id, DisplayName
├── OperationProgress.cs      Stage (Preparing/Downloading/Installing/Finalizing) + Percent (null = indeterminate)
├── OperationResult.cs        Succeeded, ErrorCode, Message (English, logs only), RebootRequired
├── OperationOptions.cs       AcceptPackageAgreements, AcceptSourceAgreements (decided by the user, not managers)
├── Agreement.cs / PackageAgreements.cs   agreements of a package and of its source (+ license, informational)
└── ErrorCodes.cs             stable codes; the UI maps each to Error_<code> strings
Operations/
├── OperationQueue.cs         single sequential queue (Channel + one worker): Enqueue/EnqueueMany (dedupes active
│                             duplicates), Cancel (queued → skipped; running → token, if the manager supports it),
│                             ClearFinished, OperationAdded/OperationChanged events (raised off the UI thread)
├── QueuedOperation.cs        state machine Queued → Running → Succeeded/Failed/Cancelled, progress, Completion task
├── IOperationHook.cs         BeforeAsync / After around each operation (App: relaunch); failures are logged only
├── OperationRequester.cs     click → queued operations; the agreements policy (see AGENTS.md invariants)
└── IAgreementsConfirmer.cs   shows agreements to the user (implemented by the App's modal)
ManagerRegistry.cs            registered managers; InitializeAsync, SearchAsync, GetUpgradesAsync fan out in parallel;
                              one manager failing is reported as a ManagerError next to the others' results
```

## WinGet manager (`src/WgetUi.Managers.WinGet`)

| File | Responsibility |
| --- | --- |
| `WinGetManager.cs` | `IPackageManager` over `Microsoft.Management.Deployment`: activation (`new PackageManager()` through the reg-free factory DLL), sources `winget` + `msstore` (predefined catalogs; `winget-font` and user sources are not exposed), search (`CatalogDefault` contains, 50 per source), upgrades (composite catalog, `LocalCatalogs`), agreements (catalog metadata + source agreements), install location (installed version metadata, read-only), install/upgrade (silent, any scope, progress, cancellation) |
| `WinGetSerialExecutor.cs` | one-at-a-time lock for every COM call; 2 min acquire timeout and 2 min query timeout (caller fails with `timeout`, the lock is released only when the COM call returns) |
| `WinGetMapping.cs` | pure mapping: unknown-version detection, `WinGetPackageData` → `Package`, progress, `InstallResultStatus` → error code, agreements, license cleanup |
| `WinGetPackageData.cs` | plain snapshot of a COM `CatalogPackage` |
| `WgetUi.Managers.WinGet.csproj` | re-exports the activation DLL and `.winmd` as content (they must sit next to the exe) |

## App (`src/WgetUi.App`)

| File | Responsibility |
| --- | --- |
| `Program.cs` | entry point: single-instance check (hand over and exit), process AppUserModelID, DI container, logging, **manager registration** (one line per manager), Platform wiring |
| `App.axaml(.cs)` | FluentAvalonia theme, styles, view-model → view templates; applies language/theme, registers notifications, creates the shell (window + tray), wires second-launch and toast activation, first-run question, initializes managers then starts the hourly check |
| `Assets/wgetui.ico` | app icon (exe + window), generated by `make icons` |
| `Icons/IconArt.cs`, `IconRenderer.cs`, `IcoFile.cs` | the artwork in a 64-unit design space (tile icon, tray glyph + state dot), PNG rendering, ICO packing (PNG entries); shared with `tools/WgetUi.IconGen` |
| `app.manifest` | `asInvoker` (never elevated) |
| `Infrastructure/AppPaths.cs` | settings and log paths (`--data-dir` override), `--tray` flag |
| `Infrastructure/SingleInstance.cs` | one instance per user + data dir: named mutex, named pipe for "activate" from a second launch |
| `Infrastructure/AppSettings.cs`, `SettingsStore.cs`, `SettingsService.cs` | settings model, JSON load/atomic save, current settings |
| `Infrastructure/FileLoggerProvider.cs` | append-only log file (rolled to `.old` past 5 MB) |
| `Localization/Loc.cs`, `TrExtension.cs` | resx lookup, live language switch, `{l:Tr Key}` markup extension, error-code → text |
| `Resources/Strings.resx`, `Strings.it.resx` | all UI strings, English (neutral) and Italian |
| `Services/AgreementsDialog.cs` | `IAgreementsConfirmer`: one modal with every agreement as a link (text in an expander when there is no link) |
| `Services/ThemeService.cs`, `IUiDispatcher.cs`, `AvaloniaUiDispatcher.cs` | theme variant, UI-thread marshalling |
| `Services/ShellHost.cs` | window show/hide, close-to-tray, Quit, activation; explicit shutdown mode. Applies `ShellCoordinator` decisions |
| `Services/TrayIconHost.cs` | Avalonia `TrayIcon` + `NativeMenu` (Open, Upgrade all (N), Check for updates, Quit); icons rendered per (taskbar theme, state) and cached |
| `Services/QuitConfirmDialog.cs`, `NotificationsConsentDialog.cs` | quit-with-pending-operations modal; first-run "notify me?" Yes/No |
| `Services/StartWithWindowsService.cs` | "Start with Windows": HKCU Run value `wgetui` = `"<exe>" --tray [--data-dir "<dir>"]`; the Run key is the source of truth |
| `Services/RelaunchPlanner.cs`, `RelaunchAfterUpgrade.cs` | `IOperationHook`: before an upgrade, record the user-session processes running from the install folder; after success + 5 s grace, restart the missing ones non-elevated with their arguments. Pure selection rules in the planner |
| `Platform/*.cs` | OS abstractions: `IToastNotifier`, `IRunKey`, `ITaskbarTheme`, `IProcessSource` (+ `ProcessSnapshot`, `IProcessLauncher`), `IWindowFlasher` |
| `Platform/Windows/*.cs` | Windows implementations: toasts + AUMID registration, HKCU Run key, `SystemUsesLightTheme`, process list (QueryFullProcessImageName, NtQueryInformationProcess, token elevation), `Process.Start`, `FlashWindowEx` |
| `Themes/SourceBadges.axaml` | badge colours (Light/Dark) and icons per source id, with a `default` fallback |
| `Controls/SourceBadge.axaml(.cs)` | the source pill used on every package row |
| `ViewModels/MainWindowViewModel.cs` | navigation (Search / Updates / Settings), "manager unavailable" banner, startup |
| `ViewModels/SearchViewModel.cs` | query, source filter, results, Install |
| `ViewModels/UpdatesViewModel.cs` | upgrades list, unknown-version toggle, Refresh, Upgrade, Upgrade all |
| `ViewModels/OperationsViewModel.cs`, `OperationItemViewModel.cs` | the queue panel (right column): visibility, collapse, pending count, cancel, clear, running flag, unseen failure (red tray dot until the panel is seen or cleared) |
| `ViewModels/SettingsViewModel.cs` | language, theme, automatic acceptance of agreements, close to tray, start with Windows, update notifications, reopen closed apps |
| `ViewModels/PackageActions.cs` | Install/Upgrade clicks → `OperationRequester` with the auto-accept setting |
| `ViewModels/TrayViewModel.cs` | tray state (dot priority admin > failure > running > updates > none), tooltip, menu texts |
| `ViewModels/ShellCoordinator.cs` | close → hide to tray (one-time hint toast) or quit; quit confirmation and cancellation of pending operations |
| `ViewModels/UpdateCheckScheduler.cs` | hourly refresh of the Updates list (`IUpdateCheckTicker`); skipped while busy or operations are pending |
| `ViewModels/UpdateNotifier.cs` | "N new updates" toast for updates not notified or seen before (key@version set in settings) |
| `ViewModels/AdminPromptWatcher.cs` | from the Installing stage, polls for consent.exe every 500 ms for 20 s: toast, taskbar flash, orange tray dot until it closes |
| `Views/*.axaml` | `MainWindow` (nav pane \| content card \| `QueuePanel`; the queue is a transparent pane outside the card, styled like the nav pane, and the card rounds both top corners), `SearchView`, `UpdatesView`, `SettingsView`, `QueuePanel` |

### Data and state

- No database. Packages are always read live from the managers; the operation queue lives in memory for the
  session.
- `settings.json` (`%APPDATA%\wgetui\settings.json`, or `<data-dir>\settings.json`), camelCase JSON:

  ```json
  {
    "language": "system", "theme": "system", "autoAcceptAgreements": false,
    "closeToTray": true, "trayHintShown": false, "notifyNewUpdates": null,
    "notifiedUpdates": ["winget/winget/7zip.7zip@26.06"], "relaunchClosedApps": true
  }
  ```

  `language`: `system` | `en` | `it`; `theme`: `system` | `light` | `dark`. Unknown values fall back to `system`.
  `notifyNewUpdates`: `null` until the first-run question is answered. `notifiedUpdates`: updates already
  notified or seen (`<package key>@<available version>`), replaced by the current list after each complete check.
  "Start with Windows" is not in the file: it is the HKCU Run value `wgetui`.
- Outside the data dir: `HKCU\Software\Classes\AppUserModelId\wgetui` (notification name + icon) and
  `%LOCALAPPDATA%\wgetui\notification-icon.png`, rewritten at every start.
- Log: `%LOCALAPPDATA%\wgetui\logs\wgetui.log` (or `<data-dir>\logs\wgetui.log`).

### Configuration

No environment variables. Command line: `wgetui.exe [--data-dir <path>] [--tray]`. `--tray` starts hidden in the
notification area (used by the Run value). A second launch with the same data dir activates the running instance
(a `--tray` one does nothing) and exits.
