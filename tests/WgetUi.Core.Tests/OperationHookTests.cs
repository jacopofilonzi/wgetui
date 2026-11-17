using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.Core.Tests;

public sealed class OperationHookTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HooksRunBeforeTheManagerAndAfterTheResult()
    {
        var manager = new FakePackageManager();
        var events = new List<string>();
        manager.OnOperation("p", (_, _, _) => { lock (events) { events.Add("manager"); } return Task.FromResult(OperationResult.Success()); });
        var hook = new RecordingHook(events);
        await using var queue = new OperationQueue(new ManagerRegistry([manager], TestLogger.For<ManagerRegistry>()), [hook], TestLogger.For<OperationQueue>());

        var op = queue.Enqueue(manager.Package("p", "1.0", "2.0"), OperationKind.Upgrade);
        await op.Completion.WaitAsync(Timeout, Ct);
        await hook.AfterCalled.Task.WaitAsync(Timeout, Ct);

        Assert.Equal(["before Running", "manager", "after Succeeded"], events);
    }

    [Fact]
    public async Task AThrowingHookNeverFailsTheOperation()
    {
        var manager = new FakePackageManager();
        await using var queue = new OperationQueue(new ManagerRegistry([manager], TestLogger.For<ManagerRegistry>()), [new ThrowingHook()], TestLogger.For<OperationQueue>());

        var op = queue.Enqueue(manager.Package("p"), OperationKind.Install);
        var next = queue.Enqueue(manager.Package("q"), OperationKind.Install);

        Assert.True((await op.Completion.WaitAsync(Timeout, Ct)).Succeeded);
        Assert.True((await next.Completion.WaitAsync(Timeout, Ct)).Succeeded);
    }

    private sealed class RecordingHook(List<string> events) : IOperationHook
    {
        public TaskCompletionSource AfterCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task BeforeAsync(QueuedOperation operation, CancellationToken cancellationToken)
        {
            lock (events)
            {
                events.Add($"before {operation.State}");
            }

            return Task.CompletedTask;
        }

        public void After(QueuedOperation operation, OperationResult result)
        {
            lock (events)
            {
                events.Add($"after {operation.State}");
            }

            AfterCalled.TrySetResult();
        }
    }

    private sealed class ThrowingHook : IOperationHook
    {
        public Task BeforeAsync(QueuedOperation operation, CancellationToken cancellationToken) => throw new InvalidOperationException("before");

        public void After(QueuedOperation operation, OperationResult result) => throw new InvalidOperationException("after");
    }
}
