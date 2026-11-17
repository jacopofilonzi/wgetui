using System.Collections.Concurrent;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.Core.Tests;

/// <summary>
/// In-memory manager for tests. Install/upgrade behaviour is scripted per package id; by default an
/// operation succeeds immediately. Never touches the machine.
/// </summary>
internal sealed class FakePackageManager : IPackageManager
{
    private readonly ConcurrentDictionary<string, Func<Package, IProgress<OperationProgress>, CancellationToken, Task<OperationResult>>> _behaviours = new();
    private int _running;

    public FakePackageManager(string id = "fake", ManagerCapabilities capabilities = ManagerCapabilities.Search | ManagerCapabilities.Install | ManagerCapabilities.ListUpgrades | ManagerCapabilities.Upgrade | ManagerCapabilities.Progress | ManagerCapabilities.Cancel)
    {
        Id = id;
        Capabilities = capabilities;
        Sources = [new PackageSource(id, "main", "Main")];
    }

    public string Id { get; }

    public string DisplayName => Id;

    public ManagerCapabilities Capabilities { get; }

    public IReadOnlyList<PackageSource> Sources { get; }

    public ManagerAvailability? Availability { get; set; }

    public ManagerAvailability InitializeResult { get; set; } = ManagerAvailability.Available;

    public Func<string, CancellationToken, Task<IReadOnlyList<Package>>>? SearchBehaviour { get; set; }

    public Func<bool, CancellationToken, Task<IReadOnlyList<Package>>>? UpgradesBehaviour { get; set; }

    /// <summary>Package ids in the order their install/upgrade started.</summary>
    public ConcurrentQueue<string> Started { get; } = new();

    public int MaxConcurrency { get; private set; }

    public Package Package(string id, string? version = "1.0", string? available = null) => new(Id, "main", id, id, version, available);

    public void OnOperation(string packageId, Func<Package, IProgress<OperationProgress>, CancellationToken, Task<OperationResult>> behaviour) =>
        _behaviours[packageId] = behaviour;

    public Task<ManagerAvailability> InitializeAsync(CancellationToken cancellationToken)
    {
        Availability = InitializeResult;
        return Task.FromResult(InitializeResult);
    }

    public Task<IReadOnlyList<Package>> SearchAsync(string query, CancellationToken cancellationToken) =>
        SearchBehaviour?.Invoke(query, cancellationToken) ?? Task.FromResult<IReadOnlyList<Package>>([]);

    public Task<IReadOnlyList<Package>> GetUpgradesAsync(bool includeUnknown, CancellationToken cancellationToken) =>
        UpgradesBehaviour?.Invoke(includeUnknown, cancellationToken) ?? Task.FromResult<IReadOnlyList<Package>>([]);

    public Task<OperationResult> InstallAsync(Package package, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        RunAsync(package, progress, cancellationToken);

    public Task<OperationResult> UpgradeAsync(Package package, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        RunAsync(package, progress, cancellationToken);

    private async Task<OperationResult> RunAsync(Package package, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        var now = Interlocked.Increment(ref _running);
        MaxConcurrency = Math.Max(MaxConcurrency, now);
        Started.Enqueue(package.Id);
        try
        {
            return _behaviours.TryGetValue(package.Id, out var behaviour)
                ? await behaviour(package, progress, cancellationToken)
                : OperationResult.Success();
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }
}
