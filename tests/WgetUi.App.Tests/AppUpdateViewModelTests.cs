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
    public async Task StartClearsOldSetupsThenChecks()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        await Vm.StartAsync();

        Assert.Equal(1, _h.Downloader.Cleanups);
        Assert.Equal(AppUpdateState.Available, Vm.State);
        Assert.True(Vm.IsVisible);
        Assert.Equal("Update wgetui", Vm.Title);
        Assert.Equal("Version 0.3.0", Vm.Subtitle);
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
        await Vm.CheckAutomaticallyAsync();

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
    }

    [Fact]
    public async Task ClickingWhileDownloadingCancels()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckAutomaticallyAsync();
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
        await Vm.CheckAutomaticallyAsync();
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
    public async Task EachNewVersionIsNotifiedOnceAndOnlyWithNotificationsOn()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        await Vm.CheckAutomaticallyAsync();
        Assert.Empty(_h.Notifier.Shown);

        _settings.Update(s => s.NotifyNewUpdates = true);
        await Vm.CheckAutomaticallyAsync();
        await Vm.CheckAutomaticallyAsync();
        Assert.Equal([("wgetui 0.3.0 is available", "Open wgetui and click \"Update wgetui\" in the menu to install it.")], _h.Notifier.Shown);
        Assert.Equal(WgetUi.App.Platform.ToastAction.OpenWindow, Assert.Single(_h.Notifier.Actions));
        Assert.Equal("0.3.0", _settings.Current.AppUpdateNotifiedVersion);

        _h.Source.Latest = FakeAppUpdateSource.Release("0.4.0");
        await Vm.CheckAutomaticallyAsync();
        Assert.Equal(2, _h.Notifier.Shown.Count);
        Assert.Equal("Version 0.4.0", Vm.Subtitle);
    }

    [Fact]
    public async Task AVersionSeenThroughTheManualCheckIsNotNotifiedLater()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");

        await Vm.CheckCommand.ExecuteAsync(null);
        await Vm.CheckAutomaticallyAsync();

        Assert.Empty(_h.Notifier.Shown);
        Assert.Equal("wgetui 0.3.0 is available: click \"Update wgetui\" in the menu.", Vm.CheckResult);
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
    }

    [Fact]
    public async Task ACheckFailureIsReportedAndKeepsWhatWasOffered()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckAutomaticallyAsync();
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

        // The script waits for the app and reopens it only for this parameter.
        Assert.Contains("UpdateParam = '/UPDATE';", script, StringComparison.Ordinal);
        Assert.Contains("/UPDATE", AppUpdateViewModel.SetupArguments.Split(' '));
        Assert.Contains("Check: IsUpdateRun", script, StringComparison.Ordinal);

        // The uninstall entry read to find the installed copy is the one this AppId creates.
        var appId = Assert.Single(script.Split('\n'), l => l.StartsWith("AppId=", StringComparison.Ordinal)).Trim();
        var guid = appId["AppId={".Length..];
        Assert.EndsWith(@"\Uninstall\" + guid + "_is1", RegistryAppInstallInfo.UninstallKey, StringComparison.Ordinal);
    }

    private async Task ReadyAsync()
    {
        _h.Source.Latest = FakeAppUpdateSource.Release("0.3.0");
        await Vm.CheckAutomaticallyAsync();
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
