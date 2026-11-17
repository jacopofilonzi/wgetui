using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.Core.Tests;

public sealed class OperationRequesterTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Agreement StoreTerms = new("Store License Terms", null, "https://aka.ms/microsoft-store-license");
    private static readonly Agreement TransactionTerms = new("Terms of Transaction", null, "https://aka.ms/microsoft-store-terms-of-transaction");
    private readonly FakePackageManager _manager = new();
    private readonly FakeConfirmer _confirmer = new();
    private readonly OperationQueue _queue;
    private readonly OperationRequester _requester;

    public OperationRequesterTests()
    {
        var registry = new ManagerRegistry([_manager], TestLogger.For<ManagerRegistry>());
        _queue = new OperationQueue(registry, TestLogger.For<OperationQueue>());
        _requester = new OperationRequester(registry, _queue, _confirmer, TestLogger.For<OperationRequester>());
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void WithStoreAgreements(string id) =>
        _manager.Agreements[id] = p => new PackageAgreements(p, [StoreTerms], [TransactionTerms]);

    private async Task<OperationRequestResult> RequestAndWaitAsync(OperationKind kind, bool autoAccept, params string[] ids)
    {
        var result = await _requester.RequestAsync([.. ids.Select(id => _manager.Package(id))], kind, autoAccept, Ct);
        await Task.WhenAll(result.Queued.Select(o => o.Completion)).WaitAsync(Timeout, Ct);
        return result;
    }

    [Fact]
    public async Task WithoutAgreementsItQueuesDirectlyWithoutAModal()
    {
        var result = await RequestAndWaitAsync(OperationKind.Install, autoAccept: false, "7zip.7zip");

        Assert.Single(result.Queued);
        Assert.False(result.Declined);
        Assert.Equal(0, _confirmer.Calls);
        Assert.Equal(OperationOptions.None, _manager.ReceivedOptions["7zip.7zip"]);
    }

    [Fact]
    public async Task AcceptingTheModalQueuesWithAgreementsAccepted()
    {
        WithStoreAgreements("9NKSQGP7F2NH");
        _confirmer.Answer = true;

        var result = await RequestAndWaitAsync(OperationKind.Install, autoAccept: false, "9NKSQGP7F2NH");

        Assert.Equal(1, _confirmer.Calls);
        Assert.Equal([StoreTerms], _confirmer.Shown.Single().PackageTerms);
        Assert.Equal([TransactionTerms], _confirmer.Shown.Single().SourceTerms);
        Assert.Single(result.Queued);
        Assert.Equal(OperationOptions.AcceptAll, _manager.ReceivedOptions["9NKSQGP7F2NH"]);
    }

    [Fact]
    public async Task CancellingTheModalQueuesNothing()
    {
        WithStoreAgreements("9NKSQGP7F2NH");
        _confirmer.Answer = false;

        var result = await RequestAndWaitAsync(OperationKind.Install, autoAccept: false, "9NKSQGP7F2NH");

        Assert.True(result.Declined);
        Assert.Empty(result.Queued);
        Assert.Empty(_queue.Operations);
        Assert.Empty(_manager.ReceivedOptions);
    }

    [Fact]
    public async Task AutoAcceptSkipsTheModalAndAcceptsEverything()
    {
        WithStoreAgreements("9NKSQGP7F2NH");

        var result = await RequestAndWaitAsync(OperationKind.Install, autoAccept: true, "9NKSQGP7F2NH", "7zip.7zip");

        Assert.Equal(0, _confirmer.Calls);
        Assert.Equal(2, result.Queued.Count);
        Assert.Equal(OperationOptions.AcceptAll, _manager.ReceivedOptions["9NKSQGP7F2NH"]);
        Assert.Equal(OperationOptions.AcceptAll, _manager.ReceivedOptions["7zip.7zip"]);
    }

    [Fact]
    public async Task InstallOfSeveralPackagesShowsOneModalWithTheAgreementsOfEveryPackageInvolved()
    {
        WithStoreAgreements("store-a");
        _manager.Agreements["store-b"] = p => new PackageAgreements(p, [StoreTerms], []);
        _confirmer.Answer = true;

        var result = await RequestAndWaitAsync(OperationKind.Install, autoAccept: false, "plain", "store-a", "store-b");

        Assert.Equal(1, _confirmer.Calls);
        Assert.Equal(["store-a", "store-b"], _confirmer.Shown.Select(a => a.Package.Id));
        Assert.Equal(["plain", "store-a", "store-b"], result.Queued.Select(o => o.Package.Id));
        Assert.Equal(OperationOptions.None, _manager.ReceivedOptions["plain"]);
        Assert.Equal(OperationOptions.AcceptAll, _manager.ReceivedOptions["store-a"]);
        Assert.Equal(new OperationOptions(true, false), _manager.ReceivedOptions["store-b"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradeNeverPromptsNorReadsAgreementsAndAcceptsThem(bool autoAccept)
    {
        WithStoreAgreements("store-a");
        _manager.Agreements["plain"] = _ => throw new InvalidOperationException("upgrades must not read agreements");

        var result = await RequestAndWaitAsync(OperationKind.Upgrade, autoAccept, "store-a");

        Assert.Equal(0, _confirmer.Calls);
        Assert.Equal(OperationKind.Upgrade, result.Queued.Single().Kind);
        Assert.Equal(OperationOptions.AcceptAll, _manager.ReceivedOptions["store-a"]);
    }

    [Fact]
    public async Task UpgradeAllNeverPromptsAndAcceptsForEveryPackage()
    {
        WithStoreAgreements("store-a");
        _manager.Agreements["plain"] = _ => throw new InvalidOperationException("upgrades must not read agreements");

        var result = await RequestAndWaitAsync(OperationKind.Upgrade, autoAccept: false, "plain", "store-a", "other");

        Assert.Equal(0, _confirmer.Calls);
        Assert.Empty(result.AgreementErrors);
        Assert.Equal(["plain", "store-a", "other"], result.Queued.Select(o => o.Package.Id));
        Assert.All(result.Queued, o => Assert.Equal(OperationOptions.AcceptAll, o.Options));
    }

    [Fact]
    public async Task UnreadableAgreementsQueueWithoutAcceptingAndReportTheError()
    {
        _manager.Agreements["broken"] = _ => throw new PackageManagerException(ErrorCodes.CatalogError, "metadata unavailable");

        var result = await RequestAndWaitAsync(OperationKind.Install, autoAccept: false, "broken");

        Assert.Equal(0, _confirmer.Calls);
        Assert.Equal(ErrorCodes.CatalogError, result.AgreementErrors.Single().ErrorCode);
        Assert.Equal(OperationOptions.None, _manager.ReceivedOptions["broken"]);
    }

    [Fact]
    public async Task ManagerWithoutAgreementCapabilityIsNotAsked()
    {
        var manager = new FakePackageManager("plain", ManagerCapabilities.Install);
        manager.Agreements["x"] = _ => throw new InvalidOperationException("must not be called");
        var registry = new ManagerRegistry([manager], TestLogger.For<ManagerRegistry>());
        await using var queue = new OperationQueue(registry, TestLogger.For<OperationQueue>());
        var requester = new OperationRequester(registry, queue, _confirmer, TestLogger.For<OperationRequester>());

        var result = await requester.RequestAsync([manager.Package("x")], OperationKind.Install, autoAcceptAgreements: false, Ct);

        Assert.Empty(result.AgreementErrors);
        Assert.Single(result.Queued);
    }

    private sealed class FakeConfirmer : IAgreementsConfirmer
    {
        public bool Answer { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyList<PackageAgreements> Shown { get; private set; } = [];

        public Task<bool> ConfirmAsync(IReadOnlyList<PackageAgreements> agreements, CancellationToken cancellationToken)
        {
            Calls++;
            Shown = agreements;
            return Task.FromResult(Answer);
        }
    }
}
