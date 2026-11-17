using System.Collections.Concurrent;
using WgetUi.App.Services;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.App.Tests;

/// <summary>Collects posted actions; the test runs them on its own thread with <see cref="Drain"/>.</summary>
internal sealed class ManualDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _pending = new();

    public void Post(Action action) => _pending.Enqueue(action);

    public void Drain()
    {
        while (_pending.TryDequeue(out var action))
        {
            action();
        }
    }
}

/// <summary>Manager whose operations complete when the test says so. Never touches the machine.</summary>
internal sealed class ScriptedManager : IPackageManager
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OperationResult>> _pending = new();
    private readonly ConcurrentDictionary<string, IProgress<OperationProgress>> _progress = new();

    public string Id => "fake";

    public string DisplayName => "Fake";

    public ManagerCapabilities Capabilities => ManagerCapabilities.ListUpgrades | ManagerCapabilities.Install | ManagerCapabilities.Upgrade | ManagerCapabilities.Cancel | ExtraCapabilities;

    public ManagerCapabilities ExtraCapabilities { get; set; }

    /// <summary>What <see cref="GetInstallLocationAsync"/> returns per package id.</summary>
    public Dictionary<string, string> InstallLocations { get; } = [];

    public Task<string?> GetInstallLocationAsync(Package package, CancellationToken cancellationToken) =>
        Task.FromResult(InstallLocations.GetValueOrDefault(package.Id));

    public IReadOnlyList<PackageSource> Sources { get; } = [new PackageSource("fake", "main", "Main")];

    public ManagerAvailability? Availability => ManagerAvailability.Available;

    /// <summary>What <see cref="GetUpgradesAsync"/> returns.</summary>
    public List<Package> Upgrades { get; } = [];

    public static Package Package(string id) => new("fake", "main", id, id, "1.0", "2.0");

    public void Complete(string id, OperationResult result) => Pending(id).TrySetResult(result);

    public Task<ManagerAvailability> InitializeAsync(CancellationToken cancellationToken) => Task.FromResult(ManagerAvailability.Available);

    /// <summary>What <see cref="SearchAsync"/> returns.</summary>
    public List<Package> SearchResults { get; } = [];

    /// <summary>What <see cref="GetInstalledAsync"/> returns.</summary>
    public List<Package> Installed { get; } = [];

    /// <summary>What <see cref="GetDetailsAsync"/> returns per package id (missing: package_not_found).</summary>
    public Dictionary<string, PackageDetails> Details { get; } = [];

    /// <summary>Options of each operation by "kind:id" ("install:a", "uninstall:b").</summary>
    public ConcurrentDictionary<string, OperationOptions> Received { get; } = new();

    public Task<IReadOnlyList<Package>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Package>>([.. SearchResults]);

    public Task<IReadOnlyList<Package>> GetInstalledAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Package>>([.. Installed]);

    public Task<PackageDetails> GetDetailsAsync(Package package, CancellationToken cancellationToken) =>
        Details.TryGetValue(package.Id, out var details)
            ? Task.FromResult(details)
            : Task.FromException<PackageDetails>(new PackageManagerException(ErrorCodes.PackageNotFound, "not scripted"));

    public Task<OperationResult> UninstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        Received["uninstall:" + package.Id] = options;
        return Pending(package.Id).Task.WaitAsync(cancellationToken);
    }

    /// <summary>How many times the upgrades were listed.</summary>
    public int UpgradeQueries { get; private set; }

    public Task<IReadOnlyList<Package>> GetUpgradesAsync(bool includeUnknown, CancellationToken cancellationToken)
    {
        UpgradeQueries++;
        return Task.FromResult<IReadOnlyList<Package>>([.. Upgrades]);
    }

    public Task<PackageAgreements> GetAgreementsAsync(Package package, CancellationToken cancellationToken) => Task.FromResult(PackageAgreements.None(package));

    public Task<OperationResult> InstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        Received["install:" + package.Id] = options;
        return Pending(package.Id).Task.WaitAsync(cancellationToken);
    }

    public Task<OperationResult> UpgradeAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        _progress[package.Id] = progress;
        return Pending(package.Id).Task.WaitAsync(cancellationToken);
    }

    /// <summary>Reports progress for a running upgrade (as winget would).</summary>
    public bool Report(string id, OperationProgress progress)
    {
        if (!_progress.TryGetValue(id, out var sink))
        {
            return false;
        }

        sink.Report(progress);
        return true;
    }

    private TaskCompletionSource<OperationResult> Pending(string id) =>
        _pending.GetOrAdd(id, _ => new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously));
}

/// <summary>In-memory Run key: tests never write the real registry.</summary>
internal sealed class FakeRunKey : WgetUi.App.Platform.IRunKey
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Read(string name) => Values.GetValueOrDefault(name);

    public void Write(string name, string command) => Values[name] = command;

    public void Delete(string name) => Values.Remove(name);
}

/// <summary>Records notifications instead of showing them.</summary>
internal sealed class FakeNotifier : WgetUi.App.Platform.IToastNotifier
{
    public event EventHandler<WgetUi.App.Platform.ToastAction>? Activated;

    public List<(string Title, string Body)> Shown { get; } = [];

    public List<WgetUi.App.Platform.ToastAction> Actions { get; } = [];

    public void Show(string title, string body, WgetUi.App.Platform.ToastAction action = WgetUi.App.Platform.ToastAction.OpenWindow)
    {
        Shown.Add((title, body));
        Actions.Add(action);
    }

    public void Click() => Activated?.Invoke(this, Actions.Count > 0 ? Actions[^1] : WgetUi.App.Platform.ToastAction.OpenWindow);
}

/// <summary>Answers the quit confirmation with a fixed value and counts the questions.</summary>
internal sealed class FixedQuitConfirmer(bool answer) : WgetUi.App.ViewModels.IQuitConfirmer
{
    public int Asked { get; private set; }

    public Task<bool> ConfirmQuitAsync(int pendingCount)
    {
        Asked++;
        return Task.FromResult(answer);
    }
}

/// <summary>A settings service on a fresh temp folder; delete <see cref="Directory"/> after the test.</summary>
internal static class TestSettings
{
    public static (WgetUi.App.Infrastructure.SettingsService Service, string Directory) Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wgetui-tests", Guid.NewGuid().ToString("N"));
        var paths = WgetUi.App.Infrastructure.AppPaths.FromArgs(["--data-dir", dir]);
        var store = new WgetUi.App.Infrastructure.SettingsStore(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<WgetUi.App.Infrastructure.SettingsStore>.Instance);
        return (new WgetUi.App.Infrastructure.SettingsService(store), dir);
    }
}

/// <summary>Agreements confirmer that is never reached in tests that only upgrade (upgrades never prompt).</summary>
internal sealed class NoAgreements : WgetUi.Core.Operations.IAgreementsConfirmer
{
    public Task<bool> ConfirmAsync(IReadOnlyList<PackageAgreements> agreements, CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>Ticker driven by the test: each <see cref="Tick"/> releases one wait.</summary>
internal sealed class ManualTicker : WgetUi.App.ViewModels.IUpdateCheckTicker
{
    private readonly System.Threading.Channels.Channel<bool> _ticks = System.Threading.Channels.Channel.CreateUnbounded<bool>();

    public void Tick() => _ticks.Writer.TryWrite(true);

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken) =>
        await _ticks.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Scripted process list.</summary>
internal sealed class FakeProcessSource : WgetUi.App.Platform.IProcessSource
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<WgetUi.App.Platform.ProcessSnapshot> Processes { get; } = [];

    public bool IsRunning(string processName) => Running.Contains(processName);

    public IReadOnlyList<WgetUi.App.Platform.ProcessSnapshot> ListSessionProcesses() => [.. Processes];
}

/// <summary>Records what would have been started. Never starts anything.</summary>
internal sealed class FakeLauncher : WgetUi.App.Platform.IProcessLauncher
{
    public List<(string Executable, string Arguments, string WorkingDirectory)> Started { get; } = [];

    public void Start(string executablePath, string arguments, string workingDirectory) => Started.Add((executablePath, arguments, workingDirectory));
}

internal sealed class FakeFlasher : WgetUi.App.Platform.IWindowFlasher
{
    public int Flashes { get; private set; }

    public void Flash() => Flashes++;
}

/// <summary>Time that only moves when the test says; timers never fire on their own.</summary>
internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public int TimersCreated { get; private set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        TimersCreated++;
        return new InertTimer();
    }

    private sealed class InertTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Records the details popups instead of showing them; <see cref="OnShow"/> plays the user's part.</summary>
internal sealed class RecordingPresenter : WgetUi.App.ViewModels.IPackageDetailsPresenter
{
    public List<WgetUi.App.ViewModels.PackageDetailsViewModel> Shown { get; } = [];

    public Func<WgetUi.App.ViewModels.PackageDetailsViewModel, Task>? OnShow { get; set; }

    public async Task ShowAsync(WgetUi.App.ViewModels.PackageDetailsViewModel details)
    {
        Shown.Add(details);
        await details.LoadAsync(CancellationToken.None);
        if (OnShow is not null)
        {
            await OnShow(details);
        }

        await details.RunChosenActionAsync();
    }

    public static WgetUi.App.ViewModels.PackageDetailsLauncher Launcher(WgetUi.Core.ManagerRegistry registry, RecordingPresenter? presenter = null) =>
        new(registry, presenter ?? new RecordingPresenter(), Microsoft.Extensions.Logging.Abstractions.NullLogger<WgetUi.App.ViewModels.PackageDetailsViewModel>.Instance);
}
