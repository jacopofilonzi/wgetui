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
│   ├── WgetUi.Core.Tests/            queue, registry, agreements policy (fake manager)
│   ├── WgetUi.Managers.WinGet.Tests/ pure mapping + serial executor (no COM activation)
│   └── WgetUi.App.Tests/             string tables (EN/IT) + queue panel view model
├── tools/WgetUi.Smoke/           opt-in live check (`make smoke`): read-only winget calls, prints results
├── WgetUi.slnx                   solution
├── Directory.Build.props         shared settings: nullable, warnings as errors, analyzers, <Version>
├── global.json                   SDK 10.0.403, test runner = Microsoft.Testing.Platform
├── .editorconfig                 code style (checked by `dotnet format` in `make check`)
└── Makefile                      dev commands (`make help`)
```

## Core (`src/WgetUi.Core`)

```text
Managers/
├── IPackageManager.cs        Id, DisplayName, Capabilities, Sources, Availability, InitializeAsync, SearchAsync,
│                             GetUpgradesAsync(includeUnknown), GetAgreementsAsync, InstallAsync/UpgradeAsync
│                             (package, OperationOptions, IProgress<OperationProgress>, ct)
├── ManagerCapabilities.cs    [Flags] Search, Install, ListUpgrades, Upgrade, Progress, Cancel,
│                             IncludeUnknownVersions, Agreements — the UI shows actions from these
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
├── OperationRequester.cs     click → queued operations; the agreements policy (see AGENTS.md invariants)
└── IAgreementsConfirmer.cs   shows agreements to the user (implemented by the App's modal)
ManagerRegistry.cs            registered managers; InitializeAsync, SearchAsync, GetUpgradesAsync fan out in parallel;
                              one manager failing is reported as a ManagerError next to the others' results
```

## WinGet manager (`src/WgetUi.Managers.WinGet`)

| File | Responsibility |
| --- | --- |
| `WinGetManager.cs` | `IPackageManager` over `Microsoft.Management.Deployment`: activation (`new PackageManager()` through the reg-free factory DLL), sources `winget` + `msstore` (predefined catalogs; `winget-font` and user sources are not exposed), search (`CatalogDefault` contains, 50 per source), upgrades (composite catalog, `LocalCatalogs`), agreements (catalog metadata + source agreements), install/upgrade (silent, any scope, progress, cancellation) |
| `WinGetSerialExecutor.cs` | one-at-a-time lock for every COM call; 2 min acquire timeout and 2 min query timeout (caller fails with `timeout`, the lock is released only when the COM call returns) |
| `WinGetMapping.cs` | pure mapping: unknown-version detection, `WinGetPackageData` → `Package`, progress, `InstallResultStatus` → error code, agreements, license cleanup |
| `WinGetPackageData.cs` | plain snapshot of a COM `CatalogPackage` |
| `WgetUi.Managers.WinGet.csproj` | re-exports the activation DLL and `.winmd` as content (they must sit next to the exe) |

## App (`src/WgetUi.App`)

| File | Responsibility |
| --- | --- |
| `Program.cs` | entry point, DI container, logging, **manager registration** (one line per manager) |
| `App.axaml(.cs)` | FluentAvalonia theme, styles, view-model → view templates; applies language/theme at startup and initializes managers |
| `app.manifest` | `asInvoker` (never elevated) |
| `Infrastructure/AppPaths.cs` | settings and log paths (`--data-dir` override) |
| `Infrastructure/AppSettings.cs`, `SettingsStore.cs`, `SettingsService.cs` | settings model, JSON load/atomic save, current settings |
| `Infrastructure/FileLoggerProvider.cs` | append-only log file (rolled to `.old` past 5 MB) |
| `Localization/Loc.cs`, `TrExtension.cs` | resx lookup, live language switch, `{l:Tr Key}` markup extension, error-code → text |
| `Resources/Strings.resx`, `Strings.it.resx` | all UI strings, English (neutral) and Italian |
| `Services/AgreementsDialog.cs` | `IAgreementsConfirmer`: one modal with every agreement as a link (text in an expander when there is no link) |
| `Services/ThemeService.cs`, `IUiDispatcher.cs`, `AvaloniaUiDispatcher.cs` | theme variant, UI-thread marshalling |
| `Themes/SourceBadges.axaml` | badge colours (Light/Dark) and icons per source id, with a `default` fallback |
| `Controls/SourceBadge.axaml(.cs)` | the source pill used on every package row |
| `ViewModels/MainWindowViewModel.cs` | navigation (Search / Updates / Settings), "manager unavailable" banner, startup |
| `ViewModels/SearchViewModel.cs` | query, source filter, results, Install |
| `ViewModels/UpdatesViewModel.cs` | upgrades list, unknown-version toggle, Refresh, Upgrade, Upgrade all |
| `ViewModels/OperationsViewModel.cs`, `OperationItemViewModel.cs` | the queue panel (right column): visibility, collapse, pending count, cancel, clear |
| `ViewModels/SettingsViewModel.cs` | language, theme, automatic acceptance of agreements |
| `ViewModels/PackageActions.cs` | Install/Upgrade clicks → `OperationRequester` with the auto-accept setting |
| `Views/*.axaml` | `MainWindow` (nav \| page \| `QueuePanel`), `SearchView`, `UpdatesView`, `SettingsView`, `QueuePanel` |

### Data and state

- No database. Packages are always read live from the managers; the operation queue lives in memory for the
  session.
- `settings.json` (`%APPDATA%\wgetui\settings.json`, or `<data-dir>\settings.json`), camelCase JSON:

  ```json
  { "language": "system", "theme": "system", "autoAcceptAgreements": false }
  ```

  `language`: `system` | `en` | `it`; `theme`: `system` | `light` | `dark`. Unknown values fall back to `system`.
- Log: `%LOCALAPPDATA%\wgetui\logs\wgetui.log` (or `<data-dir>\logs\wgetui.log`).

### Configuration

No environment variables. Command line: `wgetui.exe [--data-dir <path>]`.
