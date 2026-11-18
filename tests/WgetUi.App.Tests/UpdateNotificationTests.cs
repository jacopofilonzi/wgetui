using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class UpdateNotificationTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly FakeNotifier _toasts = new();
    private readonly UpdatesViewModel _updates;
    private readonly OperationsViewModel _operations;
    private readonly UpdateNotifier _notifier;

    public UpdateNotificationTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        var requester = new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);
        _updates = new UpdatesViewModel(registry, actions, new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance), RecordingPresenter.Launcher(registry), _queue, _dispatcher, NullLogger<UpdatesViewModel>.Instance);
        _operations = new OperationsViewModel(_queue, registry, _dispatcher);
        _notifier = new UpdateNotifier(_updates, _settings, new AutoUpdateService(_settings, NullLogger<AutoUpdateService>.Instance), _toasts, NullLogger<UpdateNotifier>.Instance);
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

    private static Package Pkg(string id, string available = "2.0") => new("fake", "main", id, id, "1.0", available);

    private Task RefreshAsync(params Package[] packages)
    {
        _manager.Upgrades.Clear();
        _manager.Upgrades.AddRange(packages);
        return _updates.RefreshCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task NothingIsShownBeforeTheFirstRunAnswerButUpdatesAreRemembered()
    {
        Assert.True(_notifier.NeedsConsent);

        await RefreshAsync(Pkg("a"));

        Assert.Empty(_toasts.Shown);
        Assert.Equal(["fake/main/a@2.0"], _settings.Current.NotifiedUpdates);
    }

    [Fact]
    public async Task NewUpdatesAreNotifiedOnceAndANewVersionIsNewsAgain()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);

        await RefreshAsync(Pkg("a"), Pkg("b"));
        await RefreshAsync(Pkg("a"), Pkg("b"));
        await RefreshAsync(Pkg("a", "3.0"), Pkg("b"));

        Assert.Equal(2, _toasts.Shown.Count);
        Assert.Equal("2 new updates available", _toasts.Shown[0].Title);
        Assert.Equal("a, b", _toasts.Shown[0].Body);
        Assert.Equal("New update available", _toasts.Shown[1].Title);
        Assert.Equal("a", _toasts.Shown[1].Body);
        Assert.All(_toasts.Actions, a => Assert.Equal(WgetUi.App.Platform.ToastAction.OpenUpdates, a));
    }

    [Fact]
    public async Task KnownSetFollowsTheListAndSurvivesAFailedManager()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);
        await RefreshAsync(Pkg("a"), Pkg("b"));

        await RefreshAsync(Pkg("b"));
        Assert.Equal(["fake/main/b@2.0"], _settings.Current.NotifiedUpdates);

        _notifier.OnLoaded([], isComplete: false);
        Assert.Equal(["fake/main/b@2.0"], _settings.Current.NotifiedUpdates);
    }

    [Fact]
    public async Task UpdatesSeenInTheActiveWindowAreNotNotified()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);
        _notifier.IsWindowActive = true;
        await RefreshAsync(Pkg("a"));

        _notifier.IsWindowActive = false;
        await RefreshAsync(Pkg("a"));

        Assert.Empty(_toasts.Shown);
    }

    [Fact]
    public async Task LongListsNameTheFirstThree()
    {
        _settings.Update(s => s.NotifyNewUpdates = true);

        await RefreshAsync(Pkg("a"), Pkg("b"), Pkg("c"), Pkg("d"), Pkg("e"));

        Assert.Equal("a, b, c and 2 more", Assert.Single(_toasts.Shown).Body);
    }

    [Fact]
    public async Task DisabledNotificationsStaySilent()
    {
        _settings.Update(s => s.NotifyNewUpdates = false);

        await RefreshAsync(Pkg("a"));

        Assert.Empty(_toasts.Shown);
    }

    [Fact]
    public async Task EachTickRefreshesTheListAndPendingOperationsSkipIt()
    {
        var ticker = new ManualTicker();
        var appUpdates = new FakeAppUpdateChecker();
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        var auto = new AutoUpdater(
            _updates,
            new AutoUpdateService(_settings, NullLogger<AutoUpdateService>.Instance),
            _settings,
            new OpenAppDetector(registry, new FakeProcessSource(), new RelaunchOptions(TimeSpan.Zero, [], 1, 1, null), NullLogger<OpenAppDetector>.Instance),
            _queue,
            _toasts,
            _dispatcher,
            NullLogger<AutoUpdater>.Instance);
        await using var scheduler = new UpdateCheckScheduler(_updates, _operations, auto, appUpdates, ticker, _dispatcher, NullLogger<UpdateCheckScheduler>.Instance);
        scheduler.Start();

        ticker.Tick();
        await UntilAsync(() => _manager.UpgradeQueries == 1);

        _queue.Enqueue(Pkg("a"), OperationKind.Upgrade);
        await UntilAsync(() => _operations.HasPending);
        ticker.Tick();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _dispatcher.Drain();

        Assert.Equal(1, _manager.UpgradeQueries);

        // wgetui's own check doesn't touch winget: it runs on both ticks, pending operations or not.
        Assert.Equal(2, appUpdates.Checks);
    }

    private async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            _dispatcher.Drain();
            if (condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Condition not reached in time.");
    }
}
