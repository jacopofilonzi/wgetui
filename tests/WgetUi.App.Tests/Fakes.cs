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

    public string Id => "fake";

    public string DisplayName => "Fake";

    public ManagerCapabilities Capabilities => ManagerCapabilities.ListUpgrades | ManagerCapabilities.Install | ManagerCapabilities.Upgrade | ManagerCapabilities.Cancel;

    public IReadOnlyList<PackageSource> Sources { get; } = [new PackageSource("fake", "main", "Main")];

    public ManagerAvailability? Availability => ManagerAvailability.Available;

    /// <summary>What <see cref="GetUpgradesAsync"/> returns.</summary>
    public List<Package> Upgrades { get; } = [];

    public static Package Package(string id) => new("fake", "main", id, id, "1.0", "2.0");

    public void Complete(string id, OperationResult result) => Pending(id).TrySetResult(result);

    public Task<ManagerAvailability> InitializeAsync(CancellationToken cancellationToken) => Task.FromResult(ManagerAvailability.Available);

    public Task<IReadOnlyList<Package>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Package>>([]);

    public Task<IReadOnlyList<Package>> GetUpgradesAsync(bool includeUnknown, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Package>>([.. Upgrades]);

    public Task<PackageAgreements> GetAgreementsAsync(Package package, CancellationToken cancellationToken) => Task.FromResult(PackageAgreements.None(package));

    public Task<OperationResult> InstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        Pending(package.Id).Task.WaitAsync(cancellationToken);

    public Task<OperationResult> UpgradeAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        Pending(package.Id).Task.WaitAsync(cancellationToken);

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
    public event EventHandler? Activated;

    public List<(string Title, string Body)> Shown { get; } = [];

    public void Show(string title, string body) => Shown.Add((title, body));

    public void Click() => Activated?.Invoke(this, EventArgs.Empty);
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
