using WgetUi.App.Infrastructure;

namespace WgetUi.App.Tests;

public sealed class InstallerMutexTests
{
    [Fact]
    public void InstallerScriptChecksTheSameNames()
    {
        var script = File.ReadAllLines(Path.Combine(RepoRoot(), "installer", "wgetui.iss"));

        var appMutex = Assert.Single(script, line => line.StartsWith("AppMutex=", StringComparison.Ordinal));
        Assert.Equal($"AppMutex={InstallerMutex.Name},{InstallerMutex.GlobalName}", appMutex.Trim());
    }

    [Fact]
    public void BothMutexesExistWhileHeld()
    {
        using var held = InstallerMutex.Acquire();

        Assert.Empty(held.Failures);
        Assert.True(Mutex.TryOpenExisting(InstallerMutex.Name, out var local));
        local.Dispose();
        Assert.True(Mutex.TryOpenExisting(InstallerMutex.GlobalName, out var global));
        global.Dispose();
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WgetUi.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (WgetUi.slnx) not found above the test output folder.");
    }
}
