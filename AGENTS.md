# AGENTS.md

Guidelines for AI coding agents (and people) working in this repository.
What lives where and how the pieces fit together is in [STRUCTURE.md](STRUCTURE.md): read it first,
instead of re-reading the whole repo.

These guidelines come from a shared template: https://github.com/jacopofilonzi/agents-guideline.
See "Updating these guidelines" below to align them with the latest version.

## What it is

wgetui: a desktop GUI for winget (search, install, upgrade, "upgrade all", installed list, uninstall, package
details, ignore an update, automatic updates per app) built so other package managers
(Chocolatey, npm…) can be added as plug-ins. Stack: C# on .NET 10, Avalonia 12 + FluentAvalonia 3 (MVVM with
CommunityToolkit.Mvvm, Microsoft.Extensions DI/logging), winget through its COM API
(Microsoft.WindowsPackageManager.ComInterop). Targets: Windows 10 1809+ / Windows 11, x64 only.

## Workflow

- **Non-trivial changes start with a plan.** For a feature, refactor, new dependency or anything touching
  several files: read what you need, then present the plan (what changes, which files, trade-offs, open
  questions) and wait for my approval before editing. Small, obvious fixes can go straight in. If the plan
  changes significantly along the way, stop and ask again.
- **Big changes go to one agent, small ones you do yourself.** The goal is to save the main conversation's
  context: the agent does the heavy reading and editing in its own context. Don't spawn sub-agents for
  everything. For a big change, once I've approved the plan, write it out in full (goal, steps, files it
  owns, constraints, how to verify) and launch a single agent to carry it out following that plan; it
  answers with a short report (what changed, which files, `make check` result, open issues), not file
  dumps. Then check it from the diff and `make check` instead of re-reading everything it read. Small
  changes you make directly in the main conversation, without agents.
- **Propose an architecture change when something outgrows its start.** A part that began small and
  simple (e.g. a dashboard in plain HTML for a couple of tasks, a single-file script, a hand-rolled
  config) may keep gaining responsibilities. When it starts to strain its original approach (state wired
  by hand, duplicated markup or logic, ever-growing glue code), stop and propose moving to a better fit
  (e.g. a framework, a proper module layout), with trade-offs and a migration path, instead of piling more
  onto the simple version. Don't migrate without my approval.
- When something is genuinely my decision (UX, user-visible naming, data formats, breaking changes), ask
  instead of guessing. Otherwise pick the sensible default and say what you picked.
- **Don't start work on a proposal until every open question has a precise answer.** When a plan or
  proposal ends with questions and the reply answers some of them but asks for clarification on others
  (or skips them), answer the clarification and wait: don't launch the agent or start editing until each
  question has an explicit answer.
- Run commands from the repo root through the Makefile (`make help` lists them). A new recurring command
  goes into the Makefile too.
- **While developing, use the framework's dev tools** (`make dev`: `dotnet watch` with hot reload) instead of building and launching the app after every change. Build and run the real artifact
  (binary, installer, Docker image) only when I say the dev tools don't pick up a specific change, and only
  for that check; then go back to the dev server.
- **Always run `make check` before declaring work done.** It must pass with zero errors and zero warnings.
  If you couldn't run something (Docker, a live API, a platform you're not on), say so instead of claiming it works.
- Makefile recipes must work in both `sh` and `cmd.exe` (GNU make on Windows falls back to `cmd.exe`): only
  `cd dir && command`, no inline `VAR=x cmd`, no `rm`/`cp`/`mkdir -p`; use `node -e` (or the project's own
  language) for file operations.
- The app has no env vars: user configuration is `settings.json` (see STRUCTURE.md). A new setting goes in
  `AppSettings` with a default that keeps the old behaviour, plus a control in Settings (EN + IT strings).
  Command-line options: `--data-dir <path>` (settings and logs in one folder, for local checks; also a separate
  single instance), `--tray` (start hidden in the notification area, used by "Start with Windows") and
  `--force-no-tray` (no tray icon, closing the window quits, `--tray` ignored: for development and agent checks).
- **Never cause real side effects without asking**: payments, bookings, emails, messages to real users,
  writes to external services. Tests use fakes and fixtures; live checks are read-only. When running the
  app locally for a check, start from an empty data directory unless told otherwise, and stop it afterwards.
  In this project these act for real on the machine and must never run in tests or checks:
  `IPackageManager.InstallAsync` / `UpgradeAsync` / `UninstallAsync` (and `WinGetManager`'s implementations),
  anything that queues them (`OperationQueue`, `OperationRequester`, the Install / Upgrade / Upgrade all /
  Uninstall buttons, "Install this version" and the primary button of the details popup), and the winget CLI
  `install`/`upgrade`/`uninstall` commands. **Automatic updates queue real upgrades on their own**:
  `AutoUpdater.RunAsync`, `UpdateCheckScheduler` (each tick and `RunAutomaticUpdatesAsync` after the startup
  check) and `UpdatesViewModel.UpgradeAutomaticallyAsync`. They act only on packages with an `autoUpdates` rule, so
  live checks start from an empty data directory and never leave a rule on (the row toggle, the details popup's
  "Update automatically"): test them with fakes. **Uninstall removes software from the user's machine**: never confirm
  the "Uninstall X?" dialog in a check. Read-only calls are fine live: activation, search, listing upgrades and
  installed packages, reading agreements, details and install locations (`make smoke`). Also real, on the user's
  machine:
  - `IToastNotifier` / `WindowsToastNotifier`: desktop notifications (a few test toasts are fine), and its
    registration under `HKCU\Software\Classes\AppUserModelId\wgetui` (written at every start, plus
    `%LOCALAPPDATA%\wgetui\notification-icon.png`);
  - `IRunKey` / `HkcuRunKey` (`StartWithWindowsService`): the HKCU `Run` value `wgetui`. Never toggle "Start
    with Windows" in a check; test it with `FakeRunKey`;
  - `IProcessLauncher` / `WindowsProcessLauncher` (`RelaunchAfterUpgrade`): starts programs. Tests use
    `FakeLauncher`; it only runs after a real upgrade or install anyway;
  - `IWindowFlasher`: flashes the taskbar button.
  When launching the app for a check use `--data-dir` with an empty scratch folder **and `--force-no-tray`** (so
  closing it never leaves an instance hidden in the tray), don't click
  Install/Upgrade/Uninstall (or confirm them), and stop it afterwards (if `make dev` is running, build and launch the Release output: the
  dev instance locks the Debug one).
- **Development never uses production credentials or services**: use test accounts, test bots, sandbox
  keys and a separate database.

### Commits

- Small, step-by-step commits as the work progresses, never a single commit at the end. Conventional
  prefixes: `feat:`, `fix:`, `refactor:`, `chore:`, `docs:`, `test:`.
- **Exception, experiments:** while we're trying things out (styling, layouts, alternative approaches),
  don't commit. Commit only once I've picked the version to keep.
- Mockups and design alternatives are never committed: they only serve to choose; the chosen one lives in the code.
- Push, tag and release only when I ask.

### Keep the agent docs in sync, in the same change

`STRUCTURE.md` and this file exist so agents don't have to re-read the whole repo. When you add, move,
rename or delete a file or module, or change an API endpoint, env var, invariant, data format or workflow
command, update `STRUCTURE.md` / `AGENTS.md` (and `README.md` for user-facing changes) in the same commit
or right after. Before declaring work done, check that every path, name and value they mention for the
areas you touched still matches the code.

### Updating these guidelines

When I ask you to check for new guidelines, fetch the latest template from
https://github.com/jacopofilonzi/agents-guideline (raw files:
`https://raw.githubusercontent.com/jacopofilonzi/agents-guideline/main/{AGENTS,CLAUDE,STRUCTURE}.md`)
and align this project's copies with it:

- Bring in new and changed generic rules (workflow, commits, code, testing…) and drop the ones removed
  upstream, without asking.
- Keep everything project-specific: filled-in placeholders, invariants, recipes, pitfalls, the whole
  content of `STRUCTURE.md`, and rules this project added or deliberately changed.
- Ask only when an upstream change conflicts with the project's current style or guidelines (e.g. it
  contradicts a project-specific rule or a convention the code follows); list each conflict with both versions.
- Then summarize what changed and commit it as `docs: sync agent guidelines`.

## Invariants — don't break these

- **Every winget COM call goes through `WinGetSerialExecutor`**, one at a time: reads that overlap an install
  return partial or empty results. Installs hold the lock for their whole duration; the timeouts only free the
  caller, never the lock.
- **Agreements are accepted only when the user did** (decided in Core by `OperationRequester`, never inside a
  manager): installs show one modal listing package/source agreements unless "Automatically accept agreements"
  is on; cancel queues nothing. Upgrades never prompt and always accept (accepted at first install). Shared
  winget catalog references never set `AcceptSourceAgreements`.
- **The UI never references a specific manager**: it reads `ManagerCapabilities` to decide which actions to show,
  and source badges come from theme resources by source id. Only `Program.cs` registers managers.
- **The app runs non-elevated** (`asInvoker` manifest): winget refuses COM activation from an elevated process.
  Installers raise their own UAC prompt.
- **Error codes are stable**: `ErrorCodes` values are never renamed; each has an `Error_<code>` string in EN and IT
  (enforced by tests). User-facing text never comes from exception messages.
- **`settings.json` stays backward compatible**: add fields with defaults, never rename or repurpose one. A file that
  fails to parse falls back to defaults instead of crashing.
- The COM activation DLL and `.winmd` must sit next to the exe (also in the single-file publish): see the WinGet
  csproj.
- **One instance per data directory** (`SingleInstance`: named mutex + pipe keyed by user and data dir). A second
  launch hands over to the first and exits; a different `--data-dir` is a separate instance.
- **The app lives in the tray**: the lifetime uses an explicit shutdown, so closing or hiding the window never
  quits. Quitting goes through `ShellHost.QuitAsync` / `ShellCoordinator.TryQuitAsync`: confirmation when
  operations are queued or running, which are then cancelled.
- **Operation hooks (`IOperationHook`) never fail or hold up an operation**: the queue logs a hook that throws;
  long work in `After` runs in the background.
- **Automatic updates go through the manual path**: they only act after a scheduled check (startup, hourly), never
  on ignored versions, on packages already queued/running, or while the global switch is off; they queue through
  `UpdatesViewModel.UpgradeAutomaticallyAsync` → `PackageActions` → `OperationRequester` like an Upgrade click.
  An open app (`OpenAppDetector`, same rules as the relaunch) is postponed unless the rule or "Update open apps too"
  forces it. Rules are keyed by package identity and gated by the `Upgrade` capability, never by manager types.
- **Relaunch is conservative**: only non-elevated processes of the user's session running from the package's
  install folder, never installers/updaters, child processes or broad folders (see `RelaunchPlanner`), and
  always started with wgetui's own (non-elevated) token.

## Code

- Everything is written in English (code, comments, commits, docs), except the language packs.
- Keep modules small and focused: one folder per area; split a file when it grows several responsibilities.
- Comments explain *why*, not *what*. Put the design of a non-trivial module in its top-level doc comment
  (upstream formats, invariants, data layout).
- Never ignore errors: wrap them with context. No `unwrap()`/`expect()`/unchecked casts on external data
  (network, files, user input).
- Errors carry a stable machine-readable code (`ErrorCodes`, plus an English message for logs); the UI maps the
  code to its own text. User-facing text never comes from an exception message.
- Use `ILogger<T>` with `[LoggerMessage]` source-generated methods (CA1848), never `Console.WriteLine` in app or
  library code (only `tools/WgetUi.Smoke` prints to the console). Logs go to `%LOCALAPPDATA%\wgetui\logs\wgetui.log`.
- Windows-only APIs (COM, `WindowsIdentity`) live in the manager projects; `WgetUi.Core` targets plain `net10.0` and
  stays OS-neutral. In the App, OS-specific code (registry, toasts, processes, P/Invoke) lives in
  `src/WgetUi.App/Platform/`: interfaces at the top level, Windows implementations in `Platform/Windows/`, wired
  only in `Program.cs`. View models and services use the interfaces, so tests use fakes. Build paths with `Path.Combine` and `Environment.GetFolderPath`, never by concatenating.
- Reuse shared clients and connections (the winget `PackageManager` and connected catalogs) instead of creating
  them per call.
- **Dependencies are recent majors whose APIs differ from most online examples:** check the docs for the
  installed version before using an API. Ask before adding a new dependency.
  Installed: .NET SDK 10.0.403 (global.json), Avalonia 12.1.6, FluentAvaloniaUI 3.1.0 (types are `FA`-prefixed:
  `FANavigationView`, `FAContentDialog`, `FAInfoBar`, `FASymbolIcon`…), CommunityToolkit.Mvvm 8.4.2 (partial
  properties: `[ObservableProperty] public partial T X { get; set; }`), Microsoft.Extensions.* 10.0.15,
  Microsoft.WindowsPackageManager.ComInterop 1.29.380 (ships its own C#/WinRT projection: no CsWinRT package),
  xunit.v3 4.0.3 on Microsoft.Testing.Platform (`dotnet test --solution`, opted in via global.json).
- Deleting user data means moving it to the trash / soft delete, unless agreed otherwise. Uninstalling a package is
  the exception the user asks for explicitly: it always goes through the "Uninstall X?" confirmation.
- Analyzers run at `AnalysisLevel latest`, `Recommended`, warnings as errors (Directory.Build.props). Fix the code;
  suppress only with a `[SuppressMessage]` that carries a justification.

### UI

- Every UI string is in `Resources/Strings.resx` (English, the fallback) **and** `Strings.it.resx`. XAML uses
  `{l:Tr Key}` (live language switch), code uses `Loc.Instance["Key"]` / `Loc.Instance.Format("Key", …)`.
  `tests/WgetUi.App.Tests` fails on a key missing in either language, a key used but not defined, or a
  placeholder mismatch. Default language: "system" (Italian on an Italian Windows, otherwise English).
- MVVM with CommunityToolkit.Mvvm: state and logic live in view models (testable with fakes); code-behind only wires
  the view (navigation selection, window backdrop). Background events reach the UI through `IUiDispatcher`.
- Compiled bindings everywhere (`x:DataType` on every view and data template).
- Colours come from theme resources (FluentAvalonia brushes via `DynamicResource`, or our own in
  `Themes/*.axaml` with Light/Dark theme dictionaries), never raw colours in view markup.
- Views never reference a manager type; actions are shown from `ManagerCapabilities`.
- Modals: confirmations use `FAContentDialog` (one open at a time). The package details popup is an in-window
  overlay (`DetailsOverlay`), and its actions run only after it closed so an install can show the agreements modal.
- Icon-only buttons get `AutomationProperties.Name` (also used to drive the UI through UI Automation in checks).

## Testing

- Unit tests live next to the code. Test parsing against fixtures copied from real responses, never live
  requests; live checks, if any, are a separate opt-in command (e.g. `make smoke`).
- Before committing a fixture taken from a real response, replace names, emails, phones and any token or ID
  that grants access to something on its own. Never commit real user files (documents, tickets, uploads).
- `tests/` holds one test project per source project. Managers are tested through pure mapping code
  (`WinGetMapping`) and fakes; COM is only exercised by the opt-in `make smoke` (read-only).
- No automated UI end-to-end tests yet: for a visual check launch the app with `--force-no-tray --data-dir` pointing at an empty
  scratch folder (`make run` / the exe), look, and stop it.

## Versions and releases

- Versions are `x.y.z`: `x` major (only when I decide), `y` feature, `z` fix. A feature bumps `y` and resets `z`.
- The version lives in one place: `<Version>` in `Directory.Build.props`.
- Tags and releases only when I ask.
- There is no CI yet. When added, it runs `make check` on every push and pull request, and builds the
  `make publish` artifact only on a `vX.Y.Z` tag or a manual run, never on a plain push.

## Adding a package manager

1. Create `src/WgetUi.Managers.<Name>/` (target `net10.0`, or `net10.0-windows…` if it needs Windows APIs)
   referencing `WgetUi.Core`, with a class implementing `IPackageManager`:
   - a stable `Id` and its `Sources` (`PackageSource(managerId, sourceId, displayName)`);
   - honest `Capabilities`: the UI hides what is not declared (`Install`, `Upgrade`, `Cancel`, `Agreements`…);
   - optional: `InstallLocation` plus an override of `GetInstallLocationAsync` (read-only) enables "reopen apps
     closed by an upgrade" for its packages; without it the default returns null and nothing is relaunched;
   - optional: `ListInstalled` + `GetInstalledAsync` (Installed page; packages no source offers use
     `PackageSource.LocalId`), `Details` + `GetDetailsAsync` (details popup), `Uninstall` + `UninstallAsync`,
     `InstallVersion` (honour `OperationOptions.TargetVersion` in `InstallAsync`). The interface defaults report
     `not_supported`, and the UI hides what is not declared;
   - queries throw `PackageManagerException(ErrorCodes.X, englishMessage)`; install/upgrade return `OperationResult`;
   - accept agreements only when the `OperationOptions` passed in say so;
   - serialize calls yourself if the tool can't run them concurrently.
2. Register it with one line in `src/WgetUi.App/Program.cs`:
   `services.AddSingleton<IPackageManager, <Name>Manager>();` and reference the project from `WgetUi.App.csproj`.
3. Optional: give its sources a badge colour/icon in `src/WgetUi.App/Themes/SourceBadges.axaml`
   (`SourceBadge.<sourceId>.Background/Foreground/Icon`, Light and Dark); otherwise the default badge is used.
4. New failure kinds: add a constant to `ErrorCodes` plus `Error_<code>` in both .resx files.
5. Add `tests/WgetUi.Managers.<Name>.Tests` for the mapping code (fixtures from real output, anonymised), add the
   projects to `WgetUi.slnx`, update `STRUCTURE.md`.

## Known pitfalls

- The main dev machine is Windows 11; shells are PowerShell and Git Bash. Commands and scripts must work there.
- **Git Bash rewrites env values that look like paths** (`BASE_PATH=/x` becomes `C:/Program Files/Git/x`):
  prefix the command with `MSYS2_ENV_CONV_EXCL='*'` (also affects path arguments such as `--data-dir`).
- A running `.exe` locks its binary on Windows: stop it before rebuilding. With close-to-tray, closing the window
  leaves a dev instance running in the tray; `make dev`/`make run` stop leftover Debug instances first (`make stop`,
  `tools/stop-dev.ps1`, only processes under this repo's `src/WgetUi.App/bin/Debug`).
- **winget COM activation fails from an elevated process** (and when App Installer is missing/outdated): the
  manager reports `com_activation_elevated` / `com_activation_failed` and the UI shows a banner. Don't "fix" it
  by requesting elevation.
- **Activation needs `Microsoft.Management.Deployment.dll` and `.winmd` next to the exe.** The ComInterop package
  copies them only into projects that reference it directly; the WinGet csproj re-exports them as content.
  Symptoms: `0x80040154 REGDB_E_CLASSNOTREG` (no DLL) or `0x80073D54` (no winmd).
- **Don't `foreach` over winget COM collections**: the C#/WinRT enumerator fails with E_NOINTERFACE; use index loops.
- **Concurrent winget calls corrupt results**: never call COM outside `WinGetSerialExecutor`.
- **Avalonia 12 / FluentAvalonia 3 differ from most online examples**: FA types are `FA`-prefixed, `TextBox.Watermark`
  is obsolete (`PlaceholderText`), `Binding` vs `ReflectionBinding`/`CompiledBinding` changed. Check the XML docs in
  `~/.nuget/packages/<pkg>/<version>/lib/` before using an API.
- **.NET 10 `dotnet test` + xunit v3 requires Microsoft.Testing.Platform** (global.json `test.runner`); VSTest mode
  errors out. Use `dotnet test --solution WgetUi.slnx` / `--project …`.
- The projects that touch COM are built for `win-x64` (RuntimeIdentifier set in the csproj): AnyCPU can't copy the
  native activation DLL.
- Stop `wgetui.exe` before rebuilding or publishing (exe lock, see above).
- **Toasts from the unpackaged exe need the HKCU AppUserModelId registration** (done at startup) and the same id
  on the process. `ToastNotifier.Setting` throws `0x80070490` until the first toast after registering: never use
  it as a gate. Without a COM activator a toast click reaches the app only while it runs.
- **Windows 11 puts a new tray icon in the overflow (^)**: look there in checks. A window started from a
  background shell opens behind the foreground app: capture it with `PrintWindow` (by handle), not a screen grab.
- **A non-elevated process can read the path and command line of the user's elevated processes**: check the
  token elevation (`ProcessSnapshot.IsElevated`) before treating a process as safe to restart.
- **`LibraryImport` needs `AllowUnsafeBlocks`** (set in the App csproj) and doesn't marshal `char[]`: pass pointers
  from `fixed` blocks. Interface members named `Get`/`Set` trip CA1716.
- **`QueuedOperation.Completion` completes just before the queue runs the `After` hooks**: tests that check hook
  effects must wait for them, not only for the completion.
- **The winget COM `Id` filter does not match local entries** (`ARP\Machine\X64\…`, `MSIX\…`): `WinGetManager.FindInstalled`
  falls back to scanning the installed list. Store apps come back from the installed catalog as local (MSIX), not msstore.
- **No public API says an MSIX package is non-removable**: `AppxNonRemovablePackages` copies what `Get-AppxPackage`
  does (`SignatureKind == System` or the undocumented `appxalluserstore!IsPackageFamilyInUninstallBlocklist`). Its
  out flag's width is unknown: pass a zeroed `int` and read the low byte (an uninitialized `out bool` marked every
  package non-removable). Inbox apps such as Calculator and Notepad are removable and must stay listed.
- **FluentAvalonia 3 has no `Info`/`Hide` symbols**: those icons are Segoe Fluent Icons / MDL2 glyphs through
  `FAFontIcon` (`FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"`).
- **`FAContentDialog` keeps an empty command band without buttons** and caps its width (548): custom-layout modals
  use an overlay instead (see `DetailsOverlay`).
- **The app started from a hidden-window shell (e.g. PowerShell `Start-Process` in a tool) opens with no visible
  window**: launch it from Git Bash, or launch it a second time with the same `--data-dir` to activate it.
