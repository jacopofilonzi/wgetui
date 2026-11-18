using WgetUi.App.Infrastructure;

namespace WgetUi.App.Tests;

public sealed class InstallerMutexTests
{
    [Fact]
    public void NamesMatchTheInstallerScript()
    {
        // installer/wgetui.iss: AppMutex=wgetui-running,Global\wgetui-running
        Assert.Equal("wgetui-running", InstallerMutex.Name);
        Assert.Equal(@"Global\wgetui-running", InstallerMutex.GlobalName);
    }

    [Fact]
    public void BothMutexesExistWhileHeld()
    {
        using (var held = InstallerMutex.Acquire())
        {
            Assert.Empty(held.Failures);
            Assert.True(Mutex.TryOpenExisting(InstallerMutex.Name, out var local));
            local.Dispose();
            Assert.True(Mutex.TryOpenExisting(InstallerMutex.GlobalName, out var global));
            global.Dispose();
        }
    }
}
