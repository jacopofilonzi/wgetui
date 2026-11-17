using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;

namespace WgetUi.App.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void KeyDependsOnTheDataDirectory()
    {
        var a = SingleInstance.KeyFor(Path.Combine(Path.GetTempPath(), "a"));

        Assert.Equal(a, SingleInstance.KeyFor(Path.Combine(Path.GetTempPath(), "A") + Path.DirectorySeparatorChar));
        Assert.NotEqual(a, SingleInstance.KeyFor(Path.Combine(Path.GetTempPath(), "b")));
    }

    [Fact]
    public async Task SecondLaunchWithTheSameKeyReachesTheFirst()
    {
        var key = "test" + Guid.NewGuid().ToString("N")[..12];
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The mutex is owned by the thread that created it: keep both on dedicated threads like two processes.
        using var first = await Task.Run(() => SingleInstance.Acquire(key), TestContext.Current.CancellationToken);
        first.MessageReceived += (_, message) => received.TrySetResult(message);
        first.Listen(NullLogger.Instance);
        var second = await Task.Run(() => SingleInstance.Acquire(key), TestContext.Current.CancellationToken);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
        Assert.True(second.Send(SingleInstance.ActivateMessage, TimeSpan.FromSeconds(5)));
        Assert.Equal(SingleInstance.ActivateMessage, await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        second.Dispose();
    }

    [Fact]
    public void DifferentKeysAreSeparateInstances()
    {
        using var a = SingleInstance.Acquire("test" + Guid.NewGuid().ToString("N")[..12]);
        using var b = SingleInstance.Acquire("test" + Guid.NewGuid().ToString("N")[..12]);

        Assert.True(a.IsPrimary);
        Assert.True(b.IsPrimary);
    }
}
