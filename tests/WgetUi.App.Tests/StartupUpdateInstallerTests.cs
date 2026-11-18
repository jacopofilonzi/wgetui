using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services.AppUpdates;

namespace WgetUi.App.Tests;

/// <summary>
/// The install at startup of a downloaded update, on fakes: the launcher only records, the downloader never
/// reads a file, the install folder is made up.
/// </summary>
public sealed class StartupUpdateInstallerTests : IDisposable
{
    private const string SetupPath = AppUpdateHarness.DownloadDirectory + @"\wgetui-setup-0.3.0-x64.exe";
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly FakeAppUpdateDownloader _downloader = new();
    private readonly FakeLauncher _launcher = new();

    public StartupUpdateInstallerTests()
    {
        (_settings, _dataDir) = TestSettings.Create();
        _settings.Update(s => s.PendingAppUpdate = Pending("0.3.0"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Fact]
    public void StartsAVerifiedNewerPendingSetupAndRecordsTheAttempt()
    {
        Assert.True(Installer().TryStart(startInTray: false));

        Assert.Equal([SetupPath], _downloader.Verified);
        var (exe, args, dir) = Assert.Single(_launcher.Started);
        Assert.Equal(SetupPath, exe);
        Assert.Equal($"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE /LOG=\"{AppUpdateHarness.DownloadDirectory}\\setup.log\"", args);
        Assert.Equal(AppUpdateHarness.DownloadDirectory, dir);
        Assert.Equal("0.3.0", _settings.Current.AppUpdateAutoInstallAttempted);
        Assert.NotNull(_settings.Current.PendingAppUpdate);
    }

    [Fact]
    public void ATrayStartAsksTheSetupToReopenInTheTray()
    {
        Assert.True(Installer().TryStart(startInTray: true));

        var (_, args, _) = Assert.Single(_launcher.Started);
        Assert.Equal($"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE /TRAY /LOG=\"{AppUpdateHarness.DownloadDirectory}\\setup.log\"", args);
    }

    [Fact]
    public void OnlyTheInstalledCopyInstallsAndItLeavesTheRecordsAlone()
    {
        Assert.False(Installer(installed: false).TryStart(startInTray: false));

        Assert.Empty(_launcher.Started);
        Assert.Empty(_downloader.Verified);
        Assert.NotNull(_settings.Current.PendingAppUpdate);
    }

    [Fact]
    public void WithAutomaticUpdatesOffNothingStarts()
    {
        _settings.Update(s => s.CheckAppUpdates = false);

        Assert.False(Installer().TryStart(startInTray: false));

        Assert.Empty(_launcher.Started);
        Assert.NotNull(_settings.Current.PendingAppUpdate);
    }

    [Fact]
    public void AFileThatNoLongerMatchesIsNeverStartedAndTheRecordIsDropped()
    {
        _downloader.VerifyResult = false;

        Assert.False(Installer().TryStart(startInTray: false));

        Assert.Empty(_launcher.Started);
        Assert.Null(_settings.Current.PendingAppUpdate);
    }

    [Fact]
    public void AnAttemptedVersionIsNotStartedAgain()
    {
        // The setup ran (or failed to) and wgetui came back still older: no loop.
        _settings.Update(s => s.AppUpdateAutoInstallAttempted = "0.3.0");

        Assert.False(Installer().TryStart(startInTray: false));

        Assert.Empty(_launcher.Started);
        Assert.NotNull(_settings.Current.PendingAppUpdate);
        Assert.Equal("0.3.0", _settings.Current.AppUpdateAutoInstallAttempted);
    }

    [Fact]
    public void ANewerPendingVersionThanTheAttemptedOneStarts()
    {
        _settings.Update(s =>
        {
            s.AppUpdateAutoInstallAttempted = "0.2.5";
            s.PendingAppUpdate = Pending("0.3.0");
        });

        Assert.True(Installer().TryStart(startInTray: false));
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    public void OnceTheRunningVersionReachesThePendingOneTheRecordsAreCleared(string running)
    {
        _settings.Update(s =>
        {
            s.PendingAppUpdate = Pending(running);
            s.AppUpdateAutoInstallAttempted = running;
        });

        Assert.False(Installer(running: running).TryStart(startInTray: false));

        Assert.Empty(_launcher.Started);
        Assert.Null(_settings.Current.PendingAppUpdate);
        Assert.Null(_settings.Current.AppUpdateAutoInstallAttempted);
    }

    [Fact]
    public void ARecordThatDoesNotNameTheSetupIsDropped()
    {
        _settings.Update(s => s.PendingAppUpdate = new PendingAppUpdate { Version = "0.3.0", FileName = @"..\evil.exe", Sha256 = new string('a', 64) });

        Assert.False(Installer().TryStart(startInTray: false));

        Assert.Empty(_launcher.Started);
        Assert.Empty(_downloader.Verified);
        Assert.Null(_settings.Current.PendingAppUpdate);
    }

    [Fact]
    public void ALaunchFailureKeepsTheAppStartingAndCountsAsAnAttempt()
    {
        _launcher.Failure = new Win32Exception(2);

        Assert.False(Installer().TryStart(startInTray: false));

        Assert.Single(_launcher.Started);
        Assert.Equal("0.3.0", _settings.Current.AppUpdateAutoInstallAttempted);
    }

    [Fact]
    public void NoPendingUpdateDoesNothing()
    {
        _settings.Update(s => s.PendingAppUpdate = null);

        Assert.False(Installer().TryStart(startInTray: true));

        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public void RecordsRoundTripThroughTheSettingsFile()
    {
        var record = StartupUpdateInstaller.ToRecord(FakeAppUpdateSource.Release("0.3.0"));
        _settings.Update(s => s.PendingAppUpdate = record);

        var reloaded = new SettingsStore(AppPaths.FromArgs(["--data-dir", _dataDir]), NullLogger<SettingsStore>.Instance).Load();
        var release = StartupUpdateInstaller.ReadPending(reloaded.PendingAppUpdate);

        Assert.NotNull(release);
        Assert.Equal((new Version(0, 3, 0), "wgetui-setup-0.3.0-x64.exe", new string('a', 64)), (release.Version, release.AssetName, release.Sha256));
    }

    private static PendingAppUpdate Pending(string version) =>
        new() { Version = version, FileName = $"wgetui-setup-{version}-x64.exe", Sha256 = new string('a', 64) };

    private StartupUpdateInstaller Installer(bool installed = true, string running = "0.2.0") =>
        new(
            _settings,
            new AppUpdateOptions(Version.Parse(running), AppUpdateHarness.InstallDirectory, AppUpdateHarness.DownloadDirectory),
            new FakeInstallInfo(installed ? AppUpdateHarness.InstallDirectory : null),
            _downloader,
            _launcher,
            NullLogger<StartupUpdateInstaller>.Instance);
}
