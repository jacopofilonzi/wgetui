using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.Managers.WinGet.Tests;

public class WinGetSerialExecutorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task QueriesWaitForARunningOperation()
    {
        using var executor = new WinGetSerialExecutor(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationStarted = new TaskCompletionSource();
        var operation = executor.RunOperationAsync("op", async () => { operationStarted.SetResult(); return await release.Task; }, Ct);
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        var queryRan = false;
        var query = executor.RunQueryAsync("q", () => queryRan = true, Ct);
        await Task.Delay(100, Ct);
        Assert.False(queryRan);

        release.SetResult(1);
        await operation;
        Assert.True(await query);
    }

    [Fact]
    public async Task WaitingTooLongForTheLockFailsWithTimeout()
    {
        using var executor = new WinGetSerialExecutor(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = executor.RunOperationAsync("op", () => release.Task, Ct);

        var ex = await Assert.ThrowsAsync<PackageManagerException>(() => executor.RunQueryAsync("q", () => 1, Ct));
        Assert.Equal(ErrorCodes.Timeout, ex.ErrorCode);
        release.SetResult(1);
        await operation;
    }

    [Fact]
    public async Task HungQueryFailsItsCallerButKeepsTheLockUntilItReturns()
    {
        using var executor = new WinGetSerialExecutor(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100));
        using var hang = new ManualResetEventSlim();

        var ex = await Assert.ThrowsAsync<PackageManagerException>(() => executor.RunQueryAsync("hung", () => hang.Wait(TimeSpan.FromSeconds(10)), Ct));
        Assert.Equal(ErrorCodes.Timeout, ex.ErrorCode);
        await Assert.ThrowsAsync<PackageManagerException>(() => executor.RunQueryAsync("next", () => 1, Ct));

        hang.Set();
        await Task.Delay(100, Ct);
        Assert.Equal(2, await executor.RunQueryAsync("after", () => 2, Ct));
    }

    [Fact]
    public async Task QueryExceptionsPropagateAndReleaseTheLock()
    {
        using var executor = new WinGetSerialExecutor(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RunQueryAsync<int>("boom", () => throw new InvalidOperationException("x"), Ct));
        Assert.Equal(3, await executor.RunQueryAsync("after", () => 3, Ct));
    }
}
