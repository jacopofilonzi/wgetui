using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class TrayAndShellTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new();
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly FakeNotifier _notifier = new();

    public TrayAndShellTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
    }

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private ShellCoordinator Coordinator(bool confirm, out FixedQuitConfirmer confirmer)
    {
        confirmer = new FixedQuitConfirmer(confirm);
        return new ShellCoordinator(_settings, _queue, confirmer, _notifier, NullLogger<ShellCoordinator>.Instance);
    }

    [Theory]
    [InlineData(false, false, false, 0, TrayIconState.None)]
    [InlineData(false, false, false, 3, TrayIconState.Updates)]
    [InlineData(false, false, true, 3, TrayIconState.Running)]
    [InlineData(false, true, true, 3, TrayIconState.Failure)]
    [InlineData(true, true, true, 3, TrayIconState.AdminPrompt)]
    [InlineData(true, false, false, 0, TrayIconState.AdminPrompt)]
    public void TrayStateFollowsPriority(bool admin, bool failure, bool running, int updates, TrayIconState expected) =>
        Assert.Equal(expected, TrayViewModel.Compute(admin, failure, running, updates));

    [Fact]
    public async Task TooltipCountsUpdatesAndQueuedOperations()
    {
        var dispatcher = new ManualDispatcher();
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _manager.Upgrades.AddRange([ScriptedManager.Package("a"), ScriptedManager.Package("b")]);
        var requester = new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);
        var updates = new UpdatesViewModel(registry, actions, new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance), RecordingPresenter.Launcher(registry), _queue, dispatcher, NullLogger<UpdatesViewModel>.Instance);
        var operations = new OperationsViewModel(_queue, registry, dispatcher);
        var tray = new TrayViewModel(updates, operations);
        WgetUi.App.Localization.Loc.Instance.SetLanguage("en");

        Assert.Equal("wgetui", tray.ToolTip);
        await updates.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("wgetui · 2 updates", tray.ToolTip);
        Assert.Equal(TrayIconState.Updates, tray.State);
        Assert.True(tray.CanCheckForUpdates);

        _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Upgrade);
        for (var i = 0; i < 100 && !operations.IsRunning; i++)
        {
            dispatcher.Drain();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal("wgetui · 2 updates · 1 queued", tray.ToolTip);
        Assert.Equal(TrayIconState.Running, tray.State);
        tray.IsAdminPromptPending = true;
        Assert.Equal(TrayIconState.AdminPrompt, tray.State);
    }

    [Fact]
    public async Task ClosingHidesToTrayAndShowsTheHintOnlyOnce()
    {
        var coordinator = Coordinator(confirm: false, out var confirmer);

        Assert.Equal(CloseOutcome.Hide, await coordinator.OnCloseRequestedAsync());
        Assert.Equal(CloseOutcome.Hide, await coordinator.OnCloseRequestedAsync());

        Assert.Single(_notifier.Shown);
        Assert.True(_settings.Current.TrayHintShown);
        Assert.Equal(0, confirmer.Asked);
    }

    [Fact]
    public async Task ClosingWithTheTrayDisabledQuitsEvenWhenCloseToTrayIsOn()
    {
        var coordinator = Coordinator(confirm: false, out var confirmer);
        coordinator.TrayDisabled = true;

        Assert.Equal(CloseOutcome.Quit, await coordinator.OnCloseRequestedAsync());
        Assert.Empty(_notifier.Shown);
        Assert.Equal(0, confirmer.Asked);
    }

    [Fact]
    public async Task ClosingWithoutTrayQuitsRightAwayWhenNothingIsPending()
    {
        _settings.Update(s => s.CloseToTray = false);
        var coordinator = Coordinator(confirm: false, out var confirmer);

        Assert.Equal(CloseOutcome.Quit, await coordinator.OnCloseRequestedAsync());
        Assert.Equal(0, confirmer.Asked);
        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public async Task QuittingWithPendingOperationsAsksAndStaysWhenDeclined()
    {
        _settings.Update(s => s.CloseToTray = false);
        var operation = _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Upgrade);
        var coordinator = Coordinator(confirm: false, out var confirmer);

        Assert.Equal(CloseOutcome.Stay, await coordinator.OnCloseRequestedAsync());
        Assert.Equal(1, confirmer.Asked);
        Assert.False(operation.IsFinished);
    }

    [Fact]
    public async Task ConfirmedQuitCancelsPendingOperations()
    {
        var running = _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Upgrade);
        var queued = _queue.Enqueue(ScriptedManager.Package("b"), OperationKind.Upgrade);
        var coordinator = Coordinator(confirm: true, out var confirmer);

        Assert.True(await coordinator.TryQuitAsync());

        Assert.Equal(1, confirmer.Asked);
        Assert.Equal(OperationState.Cancelled, queued.State);
        var result = await running.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(OperationState.Cancelled, running.State);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void StartWithWindowsWritesTheTrayCommandOnlyWhenEnabled()
    {
        var runKey = new FakeRunKey();
        var paths = AppPaths.FromArgs([]);
        var service = new StartWithWindowsService(runKey, paths, @"C:\Apps\wgetui.exe", NullLogger<StartWithWindowsService>.Instance);

        Assert.False(service.IsEnabled);
        Assert.True(service.SetEnabled(true));
        Assert.True(service.IsEnabled);
        Assert.Equal("\"C:\\Apps\\wgetui.exe\" --tray", runKey.Values[StartWithWindowsService.ValueName]);

        Assert.True(service.SetEnabled(false));
        Assert.False(service.IsEnabled);
        Assert.Empty(runKey.Values);
    }

    [Fact]
    public void StartWithWindowsKeepsAnExplicitDataDirectory()
    {
        var runKey = new FakeRunKey();
        var dir = Path.Combine(Path.GetTempPath(), "wgetui data");
        var service = new StartWithWindowsService(runKey, AppPaths.FromArgs(["--data-dir", dir]), @"C:\Apps\wgetui.exe", NullLogger<StartWithWindowsService>.Instance);

        service.SetEnabled(true);

        Assert.Equal($"\"C:\\Apps\\wgetui.exe\" --tray --data-dir \"{Path.GetFullPath(dir)}\"", runKey.Values[StartWithWindowsService.ValueName]);
    }

    [Fact]
    public void LaunchFlagsAreParsed()
    {
        var paths = AppPaths.FromArgs(["--tray", "--data-dir", "x"]);

        Assert.True(paths.StartInTray);
        Assert.Equal(Path.GetFullPath("x"), paths.ExplicitDataDirectory);
        Assert.False(AppPaths.FromArgs([]).StartInTray);
        Assert.Null(AppPaths.FromArgs([]).ExplicitDataDirectory);
    }

    [Fact]
    public void ForceNoTrayDisablesTheTrayAndOverridesTrayStart()
    {
        var paths = AppPaths.FromArgs(["--tray", "--force-no-tray"]);

        Assert.True(paths.TrayDisabled);
        Assert.False(paths.StartInTray);
        Assert.False(AppPaths.FromArgs([]).TrayDisabled);
    }

    [Fact]
    public void OldSettingsFilesGetTheNewDefaults()
    {
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"), """{ "language": "en", "theme": "dark", "autoAcceptAgreements": true }""");
        var store = new SettingsStore(AppPaths.FromArgs(["--data-dir", _dataDir]), NullLogger<SettingsStore>.Instance);

        var loaded = store.Load();

        Assert.True(loaded.AutoAcceptAgreements);
        Assert.True(loaded.CloseToTray);
        Assert.False(loaded.TrayHintShown);
    }
}
