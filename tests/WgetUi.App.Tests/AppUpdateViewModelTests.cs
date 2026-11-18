using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Platform.Windows;
using WgetUi.App.Services.AppUpdates;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

/// <summary>
/// The self-update state machine on fakes: no request to GitHub, no download, nothing started (the launcher
/// only records), no registry read.
/// </summary>
public sealed class AppUpdateViewModelTests : IAsyncDisposable
{
    private const string SetupPath = AppUpdateHarness.DownloadDirectory + @"\wgetui-setup-0.3.0-x64.exe";
    private readonly ScriptedManager _manager = new();
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly AppUpdateHarness _h;

    public AppUpdateViewModelTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        _h = new AppUpdateHarness(_settings, _queue);
        Loc.Instance.SetLanguage("en");
    }

    private AppUpdateViewModel Vm => _h.ViewModel;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Fact]
    public async Task ACopyThatIsNotTheInstalledOneNeverChecksOrShows()
    {
        var h = new AppUpdateHarness(_settings, _queue, installed: false);
        h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        await h.ViewModel.StartAsync();
        await h.ViewModel.CheckAutomaticallyAsync();

        Assert.False(h.ViewModel.IsSupported);
        Assert.False(h.ViewModel.IsVisible);
        Assert.False(h.ViewModel.CheckCommand.CanExecute(null));
        Assert.Equal(0, h.Source.Calls);
        Assert.Equal(0, h.Downloader.Cleanups);
    }

    [Fact]
    public async Task StartClearsOldSetupsThenChecksAndDownloadsInTheBackground()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        var start = Vm.StartAsync();

        Assert.Equal(1, _h.Downloader.Cleanups);
        Assert.Null(_h.Downloader.Kept);
        Assert.Equal(AppUpdateState.Downloading, Vm.State);
        _h.Downloader.Complete(SetupPath);
        await start;
        Assert.Equal(AppUpdateState.Ready, Vm.State);
        Assert.Equal("Restart to update", Vm.Title);
        Assert.Equal("Version 0.3.0", Vm.Subtitle);
    }

    [Fact]
    public async Task TheManualCheckOnlyOffersTheUpdate()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        await Vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(AppUpdateState.Available, Vm.State);
        Assert.True(Vm.IsVisible);
        Assert.Equal("Update wgetui", Vm.Title);
        Assert.Equal(0, _h.Downloader.Downloads);
        Assert.Empty(_h.Notifier.Shown);
        Assert.Null(_settings.Current.PendingAppUpdate);
        Assert.Equal("wgetui 0.3.0 is available: click \"Update wgetui\" in the menu.", Vm.CheckResult);
    }

    [Fact]
    public async Task AnAutomaticCheckDownloadsVerifiesRecordsAndNotifiesReadyOnce()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        var check = Vm.CheckAutomaticallyAsync();
        Assert.Equal(AppUpdateState.Downloading, Vm.State);
        Assert.False(Vm.IsChecking);
        _h.Downloader.Complete(SetupPath);
        await check;

        Assert.Equal(AppUpdateState.Ready, Vm.State);
        Assert.Equal([("wgetui 0.3.0 is ready: restart to update", "Click \"Restart to update\" in the wgetui menu, or it installs by itself the next time wgetui starts.")], _h.Notifier.Shown);
        Assert.Equal(WgetUi.App.Platform.ToastAction.OpenWindow, Assert.Single(_h.Notifier.Actions));
        Assert.Equal("0.3.0", _settings.Current.AppUpdateNotifiedVersion);
        var pending = Assert.IsType<PendingAppUpdate>(_settings.Current.PendingAppUpdate);
        Assert.Equal(("0.3.0", "wgetui-setup-0.3.0-x64.exe", new string('a', 64)), (pending.Version, pending.FileName, pending.Sha256));
        Assert.Empty(_h.Launcher.Started);

        // Ready: later checks change nothing and notify nothing.
        await Vm.CheckAutomaticallyAsync();
        Assert.Equal(1, _h.Downloader.Downloads);
        Assert.Single(_h.Notifier.Shown);
        Assert.Equal("wgetui 0.3.0 is ready: click \"Restart to update\" in the menu.", Vm.CheckResult);
    }

    [Fact]
    public async Task TheReadyNotificationNeedsNotificationsOnAndComesOncePerVersion()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        var check = Vm.CheckAutomaticallyAsync();
        _h.Downloader.Complete(SetupPath);
        await check;
        Assert.Empty(_h.Notifier.Shown);

        _settings.Update(s =>
        {
            s.NotifyNewUpdates = true;
            s.AppUpdateNotifiedVersion = "0.4.0";
        });
        var h = new AppUpdateHarness(_settings, _queue);
        h.Source.Latest = FakeAppUpdateSource.Release("0.4.0");
        check = h.ViewModel.CheckAutomaticallyAsync();
        h.Downloader.Complete(SetupPath);
        await check;
        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public async Task AFailedBackgroundDownloadFallsBackToAvailableQuietlyAndTheNextCheckRetries()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        var check = Vm.CheckAutomaticallyAsync();
        _h.Downloader.Fail(new AppUpdateException(ErrorCodes.AppUpdateDownloadFailed, "offline"));
        await check;

        Assert.Equal(AppUpdateState.Available, Vm.State);
        Assert.Null(Vm.ErrorCode);
        Assert.Empty(_h.Notifier.Shown);
        Assert.Null(_settings.Current.PendingAppUpdate);

        check = Vm.CheckAutomaticallyAsync();
        Assert.Equal(2, _h.Downloader.Downloads);
        _h.Downloader.Complete(SetupPath);
        await check;
        Assert.Equal(AppUpdateState.Ready, Vm.State);
        Assert.Single(_h.Notifier.Shown);
    }

    [Fact]
    public async Task StartOffersAgainAVerifiedDownloadThatWasNotInstalled()
    {
        // E.g. automatic updates were turned off after the download: no install at startup, but still Ready.
        _settings.Update(s => s.PendingAppUpdate = new PendingAppUpdate { Version = "0.3.0", FileName = "wgetui-setup-0.3.0-x64.exe", Sha256 = new string('a', 64) });

        await Vm.StartAsync();

        Assert.Equal(AppUpdateState.Ready, Vm.State);
        Assert.Equal([SetupPath], _h.Downloader.Verified);
        Assert.Equal("wgetui-setup-0.3.0-x64.exe", _h.Downloader.Kept);
        Assert.Equal(0, _h.Source.Calls);

        await Vm.ActivateAsync();
        var (exe, _, _) = Assert.Single(_h.Launcher.Started);
        Assert.Equal(SetupPath, exe);
    }

    [Fact]
    public async Task StartShowsRetryWhenThePendingInstallAlreadyFailed()
    {
        _settings.Update(s =>
        {
            s.PendingAppUpdate = new PendingAppUpdate { Version = "0.3.0", FileName = "wgetui-setup-0.3.0-x64.exe", Sha256 = new string('a', 64) };
            s.AppUpdateAutoInstallAttempted = "0.3.0";
        });
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        await Vm.StartAsync();

        Assert.Equal(AppUpdateState.Failed, Vm.State);
        Assert.Equal("Retry update", Vm.Title);
        Assert.Equal(Loc.Instance.Error(ErrorCodes.AppUpdateInstallFailed), Vm.Subtitle);
        Assert.Equal(0, _h.Downloader.Downloads);
        Assert.Equal("wgetui-setup-0.3.0-x64.exe", _h.Downloader.Kept);

        // Clicking retries the install of the same file, without downloading again.
        await Vm.ActivateAsync();
        Assert.Single(_h.Launcher.Started);
        Assert.Equal(0, _h.Downloader.Downloads);
    }

    [Fact]
    public async Task StartDropsAPendingFileThatNoLongerMatches()
    {
        _settings.Update(s => s.PendingAppUpdate = new PendingAppUpdate { Version = "0.3.0", FileName = "wgetui-setup-0.3.0-x64.exe", Sha256 = new string('a', 64) });
        _settings.Update(s => s.CheckAppUpdates = false);
        _h.Downloader.VerifyResult = false;

        await Vm.StartAsync();

        Assert.Equal(AppUpdateState.None, Vm.State);
        Assert.Null(_settings.Current.PendingAppUpdate);
        Assert.Null(_h.Downloader.Kept);
    }

    [Fact]
    public async Task UpToDateOrOlderReleasesKeepTheItemHidden()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.2.0");

        await Vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(AppUpdateState.None, Vm.State);
        Assert.False(Vm.IsVisible);
        Assert.Equal("wgetui is up to date.", Vm.CheckResult);
    }

    [Fact]
    public async Task AvailableThenDownloadingThenReadyThenInstall()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckCommand.ExecuteAsync(null);

        var download = Vm.ActivateAsync();
        Assert.Equal(AppUpdateState.Downloading, Vm.State);
        Assert.True(Vm.IsDownloading);
        Assert.Equal("Click to cancel", Vm.Subtitle);

        _h.Downloader.Progress!.Report(40);
        _h.Dispatcher.Drain();
        Assert.Equal(40, Vm.Progress);
        Assert.False(Vm.IsProgressIndeterminate);

        _h.Downloader.Complete(SetupPath);
        await download;
        Assert.Equal(AppUpdateState.Ready, Vm.State);
        Assert.Equal("Restart to update", Vm.Title);

        await Vm.ActivateAsync();

        var (exe, args, dir) = Assert.Single(_h.Launcher.Started);
        Assert.Equal(SetupPath, exe);
        Assert.Equal($"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE /LOG=\"{AppUpdateHarness.DownloadDirectory}\\setup.log\"", args);
        Assert.Equal(AppUpdateHarness.DownloadDirectory, dir);
        Assert.Equal(1, _h.QuitRequests);
        Assert.Empty(_h.Confirmer.Asked);
        Assert.Equal("0.3.0", _settings.Current.PendingAppUpdate?.Version);
        Assert.Equal("0.3.0", _settings.Current.AppUpdateAutoInstallAttempted);
    }

    [Fact]
    public async Task ClickingWhileDownloadingCancels()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckCommand.ExecuteAsync(null);
        var download = Vm.ActivateAsync();

        await Vm.ActivateAsync();
        await download;

        Assert.Equal(AppUpdateState.Available, Vm.State);
        Assert.Empty(_h.Launcher.Started);

        _ = Vm.ActivateAsync();
        Assert.Equal(2, _h.Downloader.Downloads);
    }

    [Fact]
    public async Task AHashMismatchFailsAndClickingRetriesTheDownload()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckCommand.ExecuteAsync(null);
        var download = Vm.ActivateAsync();

        _h.Downloader.Fail(new AppUpdateException(ErrorCodes.AppUpdateHashMismatch, "mismatch"));
        await download;

        Assert.Equal(AppUpdateState.Failed, Vm.State);
        Assert.Equal("Retry update", Vm.Title);
        Assert.Equal(Loc.Instance.Error(ErrorCodes.AppUpdateHashMismatch), Vm.Subtitle);

        _ = Vm.ActivateAsync();
        Assert.Equal(2, _h.Downloader.Downloads);
        Assert.Equal(AppUpdateState.Downloading, Vm.State);
    }

    [Fact]
    public async Task ALaunchFailureKeepsTheAppOpenAndRetriesTheLaunch()
    {
        await ReadyAsync();
        _h.Launcher.Failure = new Win32Exception(2);

        await Vm.ActivateAsync();

        Assert.Equal(AppUpdateState.Failed, Vm.State);
        Assert.Equal(ErrorCodes.AppUpdateLaunchFailed, Vm.ErrorCode);
        Assert.Equal(0, _h.QuitRequests);

        _h.Launcher.Failure = null;
        await Vm.ActivateAsync();

        Assert.Equal(2, _h.Launcher.Started.Count);
        Assert.Equal(1, _h.Downloader.Downloads);
        Assert.Equal(1, _h.QuitRequests);
    }

    [Fact]
    public async Task AFileChangedSinceTheDownloadIsNeverStarted()
    {
        await ReadyAsync();
        _h.Downloader.VerifyResult = false;

        await Vm.ActivateAsync();

        Assert.Empty(_h.Launcher.Started);
        Assert.Equal(ErrorCodes.AppUpdateHashMismatch, Vm.ErrorCode);

        _ = Vm.ActivateAsync();
        Assert.Equal(2, _h.Downloader.Downloads);
    }

    [Fact]
    public async Task PendingOperationsAskFirstAndDecliningDoesNothing()
    {
        await ReadyAsync();
        var operation = _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Upgrade);

        await Vm.ActivateAsync();

        Assert.Equal([(1, "0.3.0")], _h.Confirmer.Asked);
        Assert.Empty(_h.Launcher.Started);
        Assert.Equal(AppUpdateState.Ready, Vm.State);
        Assert.False(operation.IsFinished);
        Assert.Equal(0, _h.QuitRequests);
    }

    [Fact]
    public async Task ConfirmingCancelsPendingOperationsStartsTheSetupAndQuits()
    {
        await ReadyAsync();
        var operation = _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Upgrade);
        _h.Confirmer.Answer = true;

        await Vm.ActivateAsync();

        Assert.Single(_h.Launcher.Started);
        Assert.Equal(1, _h.QuitRequests);
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(OperationState.Cancelled, operation.State);
    }

    [Fact]
    public async Task WithChecksOffOnlyTheManualCheckRuns()
    {
        _settings.Update(s => s.CheckAppUpdates = false);
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        await Vm.StartAsync();
        await Vm.CheckAutomaticallyAsync();
        Assert.Equal(0, _h.Source.Calls);

        await Vm.CheckCommand.ExecuteAsync(null);
        Assert.Equal(1, _h.Source.Calls);
        Assert.Equal(AppUpdateState.Available, Vm.State);
        Assert.Equal(0, _h.Downloader.Downloads);
    }

    [Fact]
    public async Task ACheckFailureIsReportedAndKeepsWhatWasOffered()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckCommand.ExecuteAsync(null);
        _h.Source.Failure = new AppUpdateException(ErrorCodes.AppUpdateRateLimited, "403");

        await Vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(Loc.Instance.Error(ErrorCodes.AppUpdateRateLimited), Vm.CheckResult);
        Assert.Equal(AppUpdateState.Available, Vm.State);
        Assert.False(Vm.IsChecking);
    }

    [Fact]
    public async Task NoCheckWhileDownloadingOrReady()
    {
        await ReadyAsync();
        var calls = _h.Source.Calls;
        _h.Source.Latest = FakeAppUpdateSource.Release("0.4.0");

        await Vm.CheckAutomaticallyAsync();

        Assert.Equal(calls, _h.Source.Calls);
        Assert.Equal(AppUpdateState.Ready, Vm.State);
        Assert.Equal("0.3.0", Vm.Latest?.VersionText);
    }

    [Fact]
    public void SetupArgumentsMatchTheInstallerScript()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "wgetui.iss"));

        // The script waits for the app and reopens it only for /UPDATE; /TRAY picks the hidden relaunch.
        Assert.Contains("UpdateParam = '/UPDATE';", script, StringComparison.Ordinal);
        Assert.Contains($"TrayParam = '{SetupCommand.TrayParameter}';", script, StringComparison.Ordinal);
        Assert.Contains("/UPDATE", SetupCommand.BaseArguments.Split(' '));
        Assert.Contains(SetupCommand.TrayParameter, SetupCommand.Arguments("x", tray: true).Split(' '));
        Assert.DoesNotContain(SetupCommand.TrayParameter, SetupCommand.Arguments("x", tray: false).Split(' '));

        // Real [Run] entries (not the comments that mention them): one per relaunch, both telling the app it was
        // just updated, only the /TRAY one hidden in the tray, never skipped because the run is silent.
        var lines = script.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var window = Assert.Single(lines, l => l.StartsWith("Filename: \"{app}\\wgetui.exe\";", StringComparison.Ordinal) && l.Contains("Check: IsUpdateWindowRun", StringComparison.Ordinal));
        var tray = Assert.Single(lines, l => l.StartsWith("Filename: \"{app}\\wgetui.exe\";", StringComparison.Ordinal) && l.Contains("Check: IsUpdateTrayRun", StringComparison.Ordinal));
        Assert.Contains($"Parameters: \"{AppPaths.UpdatedArgument}\";", window, StringComparison.Ordinal);
        Assert.Contains($"Parameters: \"{AppPaths.TrayArgument} {AppPaths.UpdatedArgument}\";", tray, StringComparison.Ordinal);
        Assert.All(new[] { window, tray }, l => Assert.DoesNotContain("skipifsilent", l, StringComparison.Ordinal));
        Assert.All(new[] { window, tray }, l => Assert.Contains("runasoriginaluser", l, StringComparison.Ordinal));

        // The uninstall entry read to find the installed copy is the one this AppId creates.
        var appId = Assert.Single(script.Split('\n'), l => l.StartsWith("AppId=", StringComparison.Ordinal)).Trim();
        var guid = appId["AppId={".Length..];
        Assert.EndsWith(@"\Uninstall\" + guid + "_is1", RegistryAppInstallInfo.UninstallKey, StringComparison.Ordinal);
    }

    private async Task ReadyAsync()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckCommand.ExecuteAsync(null);
        var download = Vm.ActivateAsync();
        _h.Downloader.Complete(SetupPath);
        await download;
        Assert.Equal(AppUpdateState.Ready, Vm.State);
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
