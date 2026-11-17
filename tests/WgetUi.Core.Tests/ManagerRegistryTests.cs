using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.Core.Tests;

public class ManagerRegistryTests
{
    private static ManagerRegistry Registry(params IPackageManager[] managers) => new(managers, TestLogger.For<ManagerRegistry>());

    [Fact]
    public async Task SearchFansOutAndReportsTheFailingManagerWithoutFailingOthers()
    {
        var good = new FakePackageManager("good");
        good.SearchBehaviour = (q, _) => Task.FromResult<IReadOnlyList<Package>>([good.Package(q + "-1"), good.Package(q + "-2")]);
        var bad = new FakePackageManager("bad")
        {
            SearchBehaviour = (_, _) => throw new PackageManagerException(ErrorCodes.CatalogError, "catalog down"),
        };
        var throwsUnexpected = new FakePackageManager("buggy")
        {
            SearchBehaviour = (_, _) => throw new InvalidOperationException("boom"),
        };

        var result = await Registry(good, bad, throwsUnexpected).SearchAsync("7zip", TestContext.Current.CancellationToken);

        Assert.Equal(["7zip-1", "7zip-2"], result.Packages.Select(p => p.Id));
        Assert.Collection(
            result.Errors.OrderBy(e => e.ManagerId, StringComparer.Ordinal),
            e => Assert.Equal(("bad", ErrorCodes.CatalogError), (e.ManagerId, e.ErrorCode)),
            e => Assert.Equal(("buggy", ErrorCodes.Unexpected), (e.ManagerId, e.ErrorCode)));
    }

    [Fact]
    public async Task SearchRunsManagersInParallel()
    {
        var gate = new TaskCompletionSource();
        var startedA = new TaskCompletionSource();
        var startedB = new TaskCompletionSource();
        var a = new FakePackageManager("a");
        a.SearchBehaviour = async (_, _) => { startedA.SetResult(); await gate.Task; return [a.Package("x")]; };
        var b = new FakePackageManager("b");
        b.SearchBehaviour = async (_, _) => { startedB.SetResult(); await gate.Task; return [b.Package("y")]; };

        var search = Registry(a, b).SearchAsync("q", TestContext.Current.CancellationToken);
        await Task.WhenAll(startedA.Task, startedB.Task).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gate.SetResult();

        Assert.Equal(2, (await search).Packages.Count);
    }

    [Fact]
    public async Task UnavailableManagerIsReportedAndNotQueried()
    {
        var queried = false;
        var down = new FakePackageManager("down")
        {
            InitializeResult = ManagerAvailability.Unavailable(ErrorCodes.ComActivationFailed, "no server"),
            UpgradesBehaviour = (_, _) => { queried = true; return Task.FromResult<IReadOnlyList<Package>>([]); },
        };
        var up = new FakePackageManager("up");
        up.UpgradesBehaviour = (unknown, _) => Task.FromResult<IReadOnlyList<Package>>([up.Package(unknown ? "with-unknown" : "known")]);
        var registry = Registry(down, up);

        var initErrors = await registry.InitializeAsync(TestContext.Current.CancellationToken);
        var result = await registry.GetUpgradesAsync(includeUnknown: true, TestContext.Current.CancellationToken);

        Assert.Equal(("down", ErrorCodes.ComActivationFailed), (initErrors.Single().ManagerId, initErrors.Single().ErrorCode));
        Assert.False(queried);
        Assert.Equal("with-unknown", result.Packages.Single().Id);
        Assert.Equal("down", result.Errors.Single().ManagerId);
    }

    [Fact]
    public async Task ManagersWithoutTheCapabilityAreSkipped()
    {
        var installOnly = new FakePackageManager("install-only", ManagerCapabilities.Install)
        {
            SearchBehaviour = (_, _) => throw new InvalidOperationException("must not be called"),
        };

        var result = await Registry(installOnly).SearchAsync("q", TestContext.Current.CancellationToken);

        Assert.Empty(result.Packages);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DuplicateManagerIdsAreRejected() =>
        Assert.Throws<ArgumentException>(() => Registry(new FakePackageManager("x"), new FakePackageManager("x")));

    [Fact]
    public void SourcesComeFromEveryManager()
    {
        var registry = Registry(new FakePackageManager("a"), new FakePackageManager("b"));

        Assert.Equal(["a", "b"], registry.Sources.Select(s => s.ManagerId));
    }
}
