using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.Core.Tests;

public sealed class OperationQueueTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly FakePackageManager _manager = new();
    private readonly OperationQueue _queue;

    public OperationQueueTests() =>
        _queue = new OperationQueue(new ManagerRegistry([_manager], TestLogger.For<ManagerRegistry>()), TestLogger.For<OperationQueue>());

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunsOperationsOneAtATimeInEnqueueOrder()
    {
        string[] ids = ["a", "b", "c"];
        foreach (var id in ids)
        {
            _manager.OnOperation(id, async (_, _, _) => { await Task.Delay(20, Ct); return OperationResult.Success(); });
        }

        var ops = ids.Select(id => _queue.Enqueue(_manager.Package(id), OperationKind.Install)).ToList();
        await Task.WhenAll(ops.Select(o => o.Completion)).WaitAsync(Timeout, Ct);

        Assert.Equal(["a", "b", "c"], _manager.Started);
        Assert.Equal(1, _manager.MaxConcurrency);
        Assert.All(ops, o => Assert.Equal(OperationState.Succeeded, o.State));
        Assert.Equal([1L, 2L, 3L], ops.Select(o => o.Sequence));
    }

    [Fact]
    public async Task FailureDoesNotStopTheQueue()
    {
        _manager.OnOperation("fails", (_, _, _) => Task.FromResult(OperationResult.Failure(ErrorCodes.InstallFailed, "exit code 1603")));
        _manager.OnOperation("throws", (_, _, _) => throw new InvalidOperationException("bug"));

        var fails = _queue.Enqueue(_manager.Package("fails"), OperationKind.Install);
        var throws = _queue.Enqueue(_manager.Package("throws"), OperationKind.Install);
        var ok = _queue.Enqueue(_manager.Package("ok"), OperationKind.Install);
        await ok.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(OperationState.Failed, fails.State);
        Assert.Equal(ErrorCodes.InstallFailed, fails.Result?.ErrorCode);
        Assert.Equal(OperationState.Failed, throws.State);
        Assert.Equal(ErrorCodes.Unexpected, throws.Result?.ErrorCode);
        Assert.Equal(OperationState.Succeeded, ok.State);
    }

    [Fact]
    public async Task CancellingAQueuedOperationSkipsIt()
    {
        var release = new TaskCompletionSource<OperationResult>();
        var started = new TaskCompletionSource();
        _manager.OnOperation("blocker", (_, _, _) => { started.SetResult(); return release.Task; });

        var blocker = _queue.Enqueue(_manager.Package("blocker"), OperationKind.Install);
        var victim = _queue.Enqueue(_manager.Package("victim"), OperationKind.Install);
        var after = _queue.Enqueue(_manager.Package("after"), OperationKind.Install);
        await started.Task.WaitAsync(Timeout, Ct);

        _queue.Cancel(victim);
        Assert.Equal(OperationState.Cancelled, victim.State);
        Assert.False(victim.CanCancel);
        release.SetResult(OperationResult.Success());
        await after.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(["blocker", "after"], _manager.Started);
        Assert.Equal(OperationState.Succeeded, blocker.State);
        Assert.Equal(ErrorCodes.Cancelled, (await victim.Completion).ErrorCode);
    }

    [Fact]
    public async Task CancellingARunningOperationSignalsTheManager()
    {
        var started = new TaskCompletionSource();
        _manager.OnOperation("long", async (_, progress, ct) =>
        {
            progress.Report(new OperationProgress(OperationStage.Downloading, 40));
            started.SetResult();
            await Task.Delay(Timeout, ct);
            return OperationResult.Success();
        });

        var op = _queue.Enqueue(_manager.Package("long"), OperationKind.Upgrade);
        var next = _queue.Enqueue(_manager.Package("next"), OperationKind.Upgrade);
        await started.Task.WaitAsync(Timeout, Ct);
        Assert.Equal(OperationState.Running, op.State);
        Assert.Equal(40, op.Progress?.Percent);
        Assert.True(op.CanCancel);

        _queue.Cancel(op);
        await next.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(OperationState.Cancelled, op.State);
        Assert.Equal(OperationState.Succeeded, next.State);
    }

    [Fact]
    public async Task RunningOperationOfAManagerWithoutCancelCannotBeCancelled()
    {
        var manager = new FakePackageManager("nocancel", ManagerCapabilities.Install);
        await using var queue = new OperationQueue(new ManagerRegistry([manager], TestLogger.For<ManagerRegistry>()), TestLogger.For<OperationQueue>());
        var release = new TaskCompletionSource<OperationResult>();
        var started = new TaskCompletionSource();
        manager.OnOperation("p", (_, _, _) => { started.SetResult(); return release.Task; });

        var op = queue.Enqueue(manager.Package("p"), OperationKind.Install);
        await started.Task.WaitAsync(Timeout, Ct);
        Assert.False(op.CanCancel);
        queue.Cancel(op);
        release.SetResult(OperationResult.Success());

        Assert.True((await op.Completion.WaitAsync(Timeout, Ct)).Succeeded);
    }

    [Fact]
    public async Task UpgradeAllEnqueuesEveryPackageInOrder()
    {
        string[] ids = ["x", "y", "z"];
        var packages = ids.Select(id => _manager.Package(id, "1.0", "2.0")).ToList();

        var ops = _queue.EnqueueMany(packages, OperationKind.Upgrade);
        await Task.WhenAll(ops.Select(o => o.Completion)).WaitAsync(Timeout, Ct);

        Assert.All(ops, o => Assert.Equal(OperationKind.Upgrade, o.Kind));
        Assert.Equal(["x", "y", "z"], _manager.Started);
        Assert.Equal(3, _queue.Operations.Count);
    }

    [Fact]
    public async Task EnqueuingAnActiveOperationAgainReturnsTheExistingOne()
    {
        var release = new TaskCompletionSource<OperationResult>();
        _manager.OnOperation("dup", (_, _, _) => release.Task);

        var first = _queue.Enqueue(_manager.Package("dup"), OperationKind.Install);
        var second = _queue.Enqueue(_manager.Package("dup"), OperationKind.Install);
        release.SetResult(OperationResult.Success());
        await first.Completion.WaitAsync(Timeout, Ct);
        var third = _queue.Enqueue(_manager.Package("dup"), OperationKind.Install);
        await third.Completion.WaitAsync(Timeout, Ct);

        Assert.Same(first, second);
        Assert.NotSame(first, third);
    }

    [Fact]
    public async Task UnknownManagerFailsWithStableCode()
    {
        var op = _queue.Enqueue(new Package("missing", "s", "id", "name", "1"), OperationKind.Install);

        var result = await op.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(ErrorCodes.ManagerNotFound, result.ErrorCode);
        Assert.Equal(OperationState.Failed, op.State);
    }

    [Fact]
    public async Task RaisesEventsForEveryStateChange()
    {
        var states = new List<OperationState>();
        _queue.OperationChanged += (_, op) => { lock (states) { states.Add(op.State); } };

        var op = _queue.Enqueue(_manager.Package("p"), OperationKind.Install);
        await op.Completion.WaitAsync(Timeout, Ct);
        await Task.Delay(50, Ct); // the final event is raised right after completion

        lock (states)
        {
            Assert.Equal([OperationState.Running, OperationState.Succeeded], states);
        }
    }

    [Fact]
    public async Task ClearFinishedKeepsActiveOperations()
    {
        var release = new TaskCompletionSource<OperationResult>();
        _manager.OnOperation("active", (_, _, _) => release.Task);
        var done = _queue.Enqueue(_manager.Package("done"), OperationKind.Install);
        await done.Completion.WaitAsync(Timeout, Ct);
        var active = _queue.Enqueue(_manager.Package("active"), OperationKind.Install);

        var removed = _queue.ClearFinished();
        release.SetResult(OperationResult.Success());
        await active.Completion.WaitAsync(Timeout, Ct);

        Assert.Same(done, removed.Single());
        Assert.Same(active, _queue.Operations.Single());
    }
}
