using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.Core.Tests;

public sealed class UninstallAndVersionTests : IAsyncDisposable
{
    private const ManagerCapabilities Basic = ManagerCapabilities.Install | ManagerCapabilities.Upgrade | ManagerCapabilities.Agreements;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly List<OperationQueue> _queues = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var queue in _queues)
        {
            await queue.DisposeAsync();
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (OperationQueue Queue, OperationRequester Requester, CountingConfirmer Confirmer) Create(FakePackageManager manager)
    {
        var registry = new ManagerRegistry([manager], TestLogger.For<ManagerRegistry>());
        var queue = new OperationQueue(registry, TestLogger.For<OperationQueue>());
        _queues.Add(queue);
        var confirmer = new CountingConfirmer();
        return (queue, new OperationRequester(registry, queue, confirmer, TestLogger.For<OperationRequester>()), confirmer);
    }

    [Fact]
    public async Task UninstallRunsTheManagersUninstallWithoutReadingAgreements()
    {
        var manager = new FakePackageManager(capabilities: Basic | ManagerCapabilities.Uninstall);
        manager.Agreements["a"] = p => new PackageAgreements(p, [new Agreement("Terms", null, null)], []);
        var (_, requester, confirmer) = Create(manager);

        var result = await requester.RequestAsync([manager.Package("a")], OperationKind.Uninstall, autoAcceptAgreements: false, Ct);
        var operation = Assert.Single(result.Queued);
        await operation.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(["uninstall:a"], manager.Calls);
        Assert.Equal(0, confirmer.Calls);
        Assert.Equal(OperationOptions.None, manager.ReceivedOptions["a"]);
    }

    [Fact]
    public async Task UninstallOfANonRemovablePackageIsRefusedWithoutCallingTheManager()
    {
        var manager = new FakePackageManager(capabilities: Basic | ManagerCapabilities.Uninstall);
        var (queue, _, _) = Create(manager);

        var operation = queue.Enqueue(manager.Package("a") with { IsRemovable = false }, OperationKind.Uninstall);
        await operation.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(OperationState.Failed, operation.State);
        Assert.Equal(ErrorCodes.NotRemovable, operation.Result?.ErrorCode);
        Assert.Empty(manager.Calls);
    }

    [Fact]
    public async Task NonRemovablePackagesCanStillBeUpgraded()
    {
        var manager = new FakePackageManager(capabilities: Basic | ManagerCapabilities.Uninstall);
        var (queue, _, _) = Create(manager);

        var operation = queue.Enqueue(manager.Package("a") with { IsRemovable = false }, OperationKind.Upgrade);
        await operation.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(OperationState.Succeeded, operation.State);
    }

    [Fact]
    public async Task UninstallFailsAsNotSupportedWithoutTheCapability()
    {
        var manager = new FakePackageManager(capabilities: Basic);
        var (queue, _, _) = Create(manager);

        var operation = queue.Enqueue(manager.Package("a"), OperationKind.Uninstall);
        await operation.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(ErrorCodes.NotSupported, operation.Result?.ErrorCode);
        Assert.Empty(manager.Calls);
    }

    [Fact]
    public async Task TheDefaultUninstallReportsNotSupported()
    {
        IPackageManager bare = new BareManager();
        var result = await bare.UninstallAsync(new Package("bare", "main", "a", "A", "1.0"), OperationOptions.None, new Progress<OperationProgress>(), Ct);

        Assert.Equal(ErrorCodes.NotSupported, result.ErrorCode);
        var ex = await Assert.ThrowsAsync<PackageManagerException>(() => bare.GetInstalledAsync(Ct));
        Assert.Equal(ErrorCodes.NotSupported, ex.ErrorCode);
        ex = await Assert.ThrowsAsync<PackageManagerException>(() => bare.GetDetailsAsync(new Package("bare", "main", "a", "A", "1.0"), Ct));
        Assert.Equal(ErrorCodes.NotSupported, ex.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATargetVersionReachesTheInstall(bool autoAccept)
    {
        var manager = new FakePackageManager(capabilities: Basic | ManagerCapabilities.InstallVersion);
        var (_, requester, _) = Create(manager);

        var result = await requester.RequestAsync([manager.Package("a")], OperationKind.Install, autoAccept, Ct, targetVersion: "1.2.3");
        await Assert.Single(result.Queued).Completion.WaitAsync(Timeout, Ct);

        Assert.Equal("1.2.3", manager.ReceivedOptions["a"].TargetVersion);
    }

    [Fact]
    public async Task ATargetVersionIsNeverPassedToAnUpgrade()
    {
        var manager = new FakePackageManager(capabilities: Basic | ManagerCapabilities.InstallVersion);
        var (_, requester, _) = Create(manager);

        var result = await requester.RequestAsync([manager.Package("a")], OperationKind.Upgrade, false, Ct, targetVersion: "1.2.3");
        await Assert.Single(result.Queued).Completion.WaitAsync(Timeout, Ct);

        Assert.Null(manager.ReceivedOptions["a"].TargetVersion);
    }

    [Fact]
    public async Task InstallingAVersionNeedsTheCapability()
    {
        var manager = new FakePackageManager(capabilities: Basic);
        var (queue, _, _) = Create(manager);

        var operation = queue.Enqueue(manager.Package("a"), OperationKind.Install, new OperationOptions(false, false, "1.2.3"));
        await operation.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(ErrorCodes.NotSupported, operation.Result?.ErrorCode);
        Assert.Empty(manager.Calls);
    }

    [Fact]
    public async Task InstalledListingFansOutOnlyToManagersThatCanList()
    {
        var lister = new FakePackageManager("lister", Basic | ManagerCapabilities.ListInstalled);
        lister.InstalledBehaviour = _ => Task.FromResult<IReadOnlyList<Package>>([lister.Package("x"), new Package("lister", PackageSource.LocalId, "y", "Y", "1")]);
        var other = new FakePackageManager("other", Basic);
        var registry = new ManagerRegistry([lister, other], TestLogger.For<ManagerRegistry>());

        var result = await registry.GetInstalledAsync(Ct);

        Assert.Equal(["x", "y"], result.Packages.Select(p => p.Id));
        Assert.Empty(result.Errors);
    }

    private sealed class CountingConfirmer : IAgreementsConfirmer
    {
        public int Calls { get; private set; }

        public Task<bool> ConfirmAsync(IReadOnlyList<PackageAgreements> agreements, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(true);
        }
    }
}
