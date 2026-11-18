using WgetUi.App.Infrastructure;

namespace WgetUi.App.Tests;

public sealed class InstallerMutexTests
{
    [Fact]
    public void InstallerScriptChecksTheSameNames()
    {
        var script = File.ReadAllLines(Path.Combine(RepoRoot(), "installer", "wgetui.iss"));

        var appMutex = Assert.Single(script, line => line.StartsWith("AppMutex=", StringComparison.Ordinal));
        Assert.Equal($"AppMutex={InstallerMutex.Name}", appMutex.Trim());

        // The /UPDATE wait in [Code] checks the same mutex before the AppMutex check runs.
        var codeConstant = Assert.Single(script, line => line.TrimStart().StartsWith("AppMutexName =", StringComparison.Ordinal));
        Assert.Equal($"AppMutexName = '{InstallerMutex.Name}';", codeConstant.Trim());
    }

    [Fact]
    public void MutexExistsWhileHeld()
    {
        using var held = InstallerMutex.Acquire();

        Assert.Empty(held.Failures);
        Assert.True(Mutex.TryOpenExisting(InstallerMutex.Name, out var local));
        local.Dispose();
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
