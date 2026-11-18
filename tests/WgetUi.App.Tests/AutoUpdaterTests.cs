using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Platform;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

/// <summary>Automatic updates with a scripted manager, process list and notifier: nothing touches the machine.</summary>
public sealed class AutoUpdaterTests : IAsyncDisposable
{
    private const int Session = 1;
    private readonly ScriptedManager _manager = new() { ExtraCapabilities = ManagerCapabilities.InstallLocation };
    private readonly ManualDispatcher _dispatcher = new();
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly FakeNotifier _toasts = new();
    private readonly FakeProcessSource _processes = new();
    private readonly IgnoredUpdatesService _ignored;
    private readonly AutoUpdateService _rules;
    private readonly UpdatesViewModel _updates;
    private readonly OperationsViewModel _operations;
    private readonly UpdateNotifier _newUpdates;
    private readonly AutoUpdater _auto;

    public AutoUpdaterTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        _ignored = new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance);
        _rules = new AutoUpdateService(_settings, NullLogger<AutoUpdateService>.Instance);
        var requester = new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);
        _updates = new UpdatesViewModel(registry, actions, _ignored, RecordingPresenter.Launcher(registry), _queue, _dispatcher, NullLogger<UpdatesViewModel>.Instance);
        _operations = new OperationsViewModel(_queue, registry, _dispatcher);
        _newUpdates = new UpdateNotifier(_updates, _settings, _rules, _toasts, NullLogger<UpdateNotifier>.Instance);
        var detector = new OpenAppDetector(registry, _processes, new RelaunchOptions(TimeSpan.Zero, [], 999, Session, @"C:\Tools\wgetui.exe"), NullLogger<OpenAppDetector>.Instance);
        _auto = new AutoUpdater(_updates, _rules, _settings, detector, _queue, _toasts, _dispatcher, NullLogger<AutoUpdater>.Instance);
        _manager.InstallLocations["a"] = @"C:\Apps\A";
        _manager.InstallLocations["b"] = @"C:\Apps\B";
        Loc.Instance.SetLanguage("en");
    }

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Package Pkg(string id, string available = "2.0") => new("fake", "main", id, id.ToUpperInvariant(), "1.0", available);

    private void Open(string id) => _processes.Processes.Add(new ProcessSnapshot(
        100 + _processes.Processes.Count, null, Session, $@"C:\Apps\{id.ToUpperInvariant()}\{id}.exe", $@"""C:\Apps\{id.ToUpperInvariant()}\{id}.exe""", IsElevated: false));

    private async Task<IReadOnlyList<QueuedOperation>> CheckAsync(params Package[] packages)
    {
        _manager.Upgrades.Clear();
        _manager.Upgrades.AddRange(packages);
        await _updates.RefreshCommand.ExecuteAsync(null);
        return await _auto.RunAsync(Ct);
    }

    private async Task FinishAsync(IReadOnlyList<QueuedOperation> queued)
    {
        await _auto.LastReport.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        _dispatcher.Drain();
        Assert.All(queued, q => Assert.True(q.IsFinished));
    }

    [Fact]
    public async Task PackagesWithoutARuleAreNeverQueued()
    {
        _rules.Set(Pkg("b"), enabled: true);

        var queued = await CheckAsync(Pkg("a"));

        Assert.Empty(queued);
        Assert.Empty(_queue.Operations);
        Assert.Empty(_toasts.Shown);
    }

    [Fact]
    public async Task AClosedAppIsQueuedLikeAManualUpgrade()
    {
        _rules.Set(Pkg("a"), enabled: true);
        Open("b"); // another app is open, not this one

        var queued = await CheckAsync(Pkg("a"), Pkg("b"));

        var operation = Assert.Single(queued);
        Assert.Equal(("a", OperationKind.Upgrade), (operation.Package.Id, operation.Kind));
        Assert.True(operation.Options.AcceptPackageAgreements); // upgrades always accept, like a manual Upgrade
        Assert.True(_updates.Upgrades.Single(r => r.Id == "a").IsQueued);
        Assert.False(_updates.Upgrades.Single(r => r.Id == "b").IsQueued);
    }

    [Fact]
    public async Task AnOpenAppIsPostponedAndNotifiedOncePerVersion()
    {
        _rules.Set(Pkg("a"), enabled: true);
        Open("a");

        Assert.Empty(await CheckAsync(Pkg("a")));
        Assert.Empty(await CheckAsync(Pkg("a")));

        var toast = Assert.Single(_toasts.Shown);
        Assert.Equal("Update postponed: A 2.0", toast.Title);
        Assert.Equal(ToastAction.OpenUpdates, Assert.Single(_toasts.Actions));
        Assert.Equal(["fake/main/a@2.0"], _settings.Current.PostponedUpdates);
        Assert.Empty(_queue.Operations);

        Assert.Empty(await CheckAsync(Pkg("a", "3.0")));

        Assert.Equal(2, _toasts.Shown.Count);
        Assert.Equal("Update postponed: A 3.0", _toasts.Shown[1].Title);
        Assert.Equal(["fake/main/a@3.0"], _settings.Current.PostponedUpdates);
    }

    [Fact]
    public async Task PostponedRecordsAreDroppedOnceTheUpdateIsGone()
    {
        _rules.Set(Pkg("a"), enabled: true);
        Open("a");
        await CheckAsync(Pkg("a"));

        _processes.Processes.Clear();
        _manager.Upgrades.Clear();
        await _updates.RefreshCommand.ExecuteAsync(null);
        await _auto.RunAsync(Ct);

        Assert.Empty(_settings.Current.PostponedUpdates);
    }

    [Fact]
    public async Task TheAppBecomesQueuedOnceItIsClosed()
    {
        _rules.Set(Pkg("a"), enabled: true);
        Open("a");
        await CheckAsync(Pkg("a"));

        _processes.Processes.Clear();

        Assert.Single(await CheckAsync(Pkg("a")));
    }

    [Fact]
    public async Task ForcePerAppQueuesEvenWhenOpen()
    {
        _rules.Set(Pkg("a"), enabled: true, force: true);
        Open("a");

        Assert.Single(await CheckAsync(Pkg("a")));
        Assert.Empty(_toasts.Shown);
    }

    [Fact]
    public async Task GlobalForceQueuesEveryOpenApp()
    {
        _rules.Set(Pkg("a"), enabled: true);
        _settings.Update(s => s.AutoUpdateOpenApps = true);
        Open("a");

        Assert.Single(await CheckAsync(Pkg("a")));
    }

    [Fact]
    public async Task TheGlobalSwitchPausesEverything()
    {
        _rules.Set(Pkg("a"), enabled: true, force: true);
        _settings.Update(s => s.AutoUpdatesEnabled = false);

        Assert.Empty(await CheckAsync(Pkg("a")));
        Assert.Empty(_queue.Operations);
        Assert.Single(_rules.Rules);
    }

    [Fact]
    public async Task IgnoredVersionsAreNotUpdated()
    {
        _rules.Set(Pkg("a"), enabled: true);
        _ignored.Ignore(Pkg("a"));

        Assert.Empty(await CheckAsync(Pkg("a")));

        Assert.Single(await CheckAsync(Pkg("a", "3.0")));
    }

    [Fact]
    public async Task AlreadyQueuedPackagesAreNotQueuedTwice()
    {
        _rules.Set(Pkg("a"), enabled: true);
        Assert.Single(await CheckAsync(Pkg("a")));

        Assert.Empty(await _auto.RunAsync(Ct));
        Assert.Single(_queue.Operations);
    }

    [Fact]
    public async Task AFinishedBatchIsSummarizedAndCancelledOnesAreNotReported()
    {
        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            _rules.Set(Pkg(id), enabled: true);
        }

        var queued = await CheckAsync(Pkg("a"), Pkg("b"), Pkg("c"), Pkg("d"));
        Assert.Equal(4, queued.Count);
        _manager.Complete("a", OperationResult.Success());
        _manager.Complete("b", OperationResult.Success());
        _manager.Complete("c", OperationResult.Failure(ErrorCodes.InstallFailed, "boom"));
        _queue.Cancel(queued.Single(q => q.Package.Id == "d"));

        await FinishAsync(queued);

        Assert.Equal(2, _toasts.Shown.Count);
        Assert.Equal("Updated automatically: A, B", _toasts.Shown[0].Title);
        Assert.Equal("Automatic update of C failed", _toasts.Shown[1].Title);
        Assert.All(_toasts.Actions, a => Assert.Equal(ToastAction.OpenUpdates, a));
    }

    [Fact]
    public async Task SeveralFailuresShareOneToast()
    {
        _rules.Set(Pkg("a"), enabled: true);
        _rules.Set(Pkg("b"), enabled: true);
        var queued = await CheckAsync(Pkg("a"), Pkg("b"));
        _manager.Complete("a", OperationResult.Failure(ErrorCodes.InstallFailed, "boom"));
        _manager.Complete("b", OperationResult.Failure(ErrorCodes.InstallFailed, "boom"));

        await FinishAsync(queued);

        Assert.Equal("Automatic updates failed: A, B", Assert.Single(_toasts.Shown).Title);
    }

    [Fact]
    public async Task AutomaticUpdatesAreNotAlsoAnnouncedAsNewUpdates()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);
        _rules.Set(Pkg("a"), enabled: true);
        Open("a");

        await CheckAsync(Pkg("a"), Pkg("b"));

        Assert.Equal(2, _toasts.Shown.Count);
        Assert.Equal("New update available", _toasts.Shown[0].Title);
        Assert.Equal("B", _toasts.Shown[0].Body);
        Assert.Equal("Update postponed: A 2.0", _toasts.Shown[1].Title);
    }

    [Fact]
    public async Task WithoutAnInstallFolderTheAppCountsAsClosed()
    {
        _manager.InstallLocations.Remove("a");
        _rules.Set(Pkg("a"), enabled: true);
        Open("a");

        Assert.Single(await CheckAsync(Pkg("a")));
    }

    [Fact]
    public async Task TheSchedulerRunsAutomaticUpdatesAfterEachCheck()
    {
        _rules.Set(Pkg("a"), enabled: true);
        _manager.Upgrades.Add(Pkg("a"));
        var ticker = new ManualTicker();
        await using var scheduler = new UpdateCheckScheduler(_updates, _operations, _auto, ticker, _dispatcher, NullLogger<UpdateCheckScheduler>.Instance);
        scheduler.Start();

        ticker.Tick();
        for (var i = 0; i < 200 && _queue.Operations.Count == 0; i++)
        {
            _dispatcher.Drain();
            await Task.Delay(25, Ct);
        }

        Assert.Equal("a", Assert.Single(_queue.Operations).Package.Id);
        GC.KeepAlive(_newUpdates);
    }
}
