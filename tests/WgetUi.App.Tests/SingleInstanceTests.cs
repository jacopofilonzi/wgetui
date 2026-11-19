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
        var received = new TaskCompletionSource<InstanceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The mutex is owned by the thread that created it: keep both on dedicated threads like two processes.
        using var first = await Task.Run(() => SingleInstance.Acquire(key), TestContext.Current.CancellationToken);
        first.MessageReceived += (_, message) => received.TrySetResult(message);
        first.Listen(NullLogger.Instance);
        var second = await Task.Run(() => SingleInstance.Acquire(key), TestContext.Current.CancellationToken);
        var import = Path.Combine(Path.GetTempPath(), "my list.wgetui");

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
        Assert.True(second.Send(new InstanceMessage(SingleInstance.ActivateMessage, import), TimeSpan.FromSeconds(5)));
        Assert.Equal(new InstanceMessage(SingleInstance.ActivateMessage, import), await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
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

    [Fact]
    public void MessagesRoundTripAndStayReadableAcrossVersions()
    {
        static InstanceMessage? Read(string text) => InstanceMessage.Parse([.. text.Split('\n').Where(l => l.Length > 0)]);
        const string List = @"C:\Users\x\list.wgetui";

        Assert.Equal(new InstanceMessage("activate"), Read(new InstanceMessage("activate").Format()));
        Assert.Equal(new InstanceMessage("activate", List), Read(new InstanceMessage("activate", List).Format()));

        // An older wgetui only reads the first line: the command comes first, so it still activates.
        Assert.StartsWith("activate\n", new InstanceMessage("activate", List).Format(), StringComparison.Ordinal);

        // Unknown or empty lines from a newer wgetui are skipped; no command is no message.
        Assert.Equal(new InstanceMessage("tray"), InstanceMessage.Parse(["tray", "color:blue", "import:"]));
        Assert.Null(InstanceMessage.Parse([]));
        Assert.Null(InstanceMessage.Parse([" "]));
    }

    [Fact]
    public void ALaunchWithAnImportAlwaysActivates()
    {
        var paths = AppPaths.FromArgs([AppPaths.TrayArgument, AppPaths.ImportArgument, "list.wgetui"]);

        Assert.False(paths.StartInTray);
        Assert.Equal(Path.GetFullPath("list.wgetui"), paths.ImportPath);
        Assert.Equal(new InstanceMessage(SingleInstance.ActivateMessage, Path.GetFullPath("list.wgetui")), InstanceMessage.For(paths));
        Assert.Equal(new InstanceMessage(SingleInstance.TrayMessage), InstanceMessage.For(AppPaths.FromArgs([AppPaths.TrayArgument])));
    }

    [Fact]
    public void ImportIsParsedNextToTheOtherOptions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wgetui-data");
        var paths = AppPaths.FromArgs(["--IMPORT", @"C:\Lists\a.wgetui", "--data-dir", dir]);

        Assert.Equal(@"C:\Lists\a.wgetui", paths.ImportPath);
        Assert.Equal(Path.GetFullPath(dir), paths.ExplicitDataDirectory);
        Assert.Null(AppPaths.FromArgs([]).ImportPath);
        Assert.Null(AppPaths.FromArgs([AppPaths.ImportArgument]).ImportPath);
        Assert.Null(AppPaths.FromArgs([AppPaths.ImportArgument, "  "]).ImportPath);
    }
}
