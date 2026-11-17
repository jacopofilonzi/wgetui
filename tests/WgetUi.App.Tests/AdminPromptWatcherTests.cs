using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Localization;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class AdminPromptWatcherTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly OperationQueue _queue;
    private readonly TrayViewModel _tray;
    private readonly FakeProcessSource _processes = new();
    private readonly FakeNotifier _toasts = new();
    private readonly FakeFlasher _flasher = new();
    private readonly ManualTime _time = new();
    private readonly AdminPromptWatcher _watcher;
    private readonly string _dataDir;

    public AdminPromptWatcherTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        var (settings, dataDir) = TestSettings.Create();
        _dataDir = dataDir;
        var requester = new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, settings, NullLogger<PackageActions>.Instance);
        var updates = new UpdatesViewModel(registry, actions, _queue, _dispatcher, NullLogger<UpdatesViewModel>.Instance);
        _tray = new TrayViewModel(updates, new OperationsViewModel(_queue, registry, _dispatcher));
        _watcher = new AdminPromptWatcher(_queue, _tray, _processes, _toasts, _flasher, _dispatcher, _time, NullLogger<AdminPromptWatcher>.Instance);
        Loc.Instance.SetLanguage("en");
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        await _queue.DisposeAsync();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private DateTimeOffset At(double seconds) => _time.Now.AddSeconds(seconds);

    [Fact]
    public void PromptWithinTheWindowNotifiesOnceAndClearsWhenItCloses()
    {
        _watcher.Watch(1, "Docker Desktop", At(0));

        _watcher.Poll(At(1));
        Assert.Empty(_toasts.Shown);

        _processes.Running.Add(AdminPromptWatcher.ConsentProcess);
        _watcher.Poll(At(5));
        _watcher.Poll(At(5.5));
        _dispatcher.Drain();

        Assert.Equal("Docker Desktop needs administrator permission", Assert.Single(_toasts.Shown).Title);
        Assert.Equal(1, _flasher.Flashes);
        Assert.True(_watcher.IsPromptPending);
        Assert.True(_tray.IsAdminPromptPending);
        Assert.Equal(TrayIconState.AdminPrompt, _tray.State);

        // Still open after the 20 s window: stays orange until it is gone.
        _watcher.Poll(At(30));
        _dispatcher.Drain();
        Assert.True(_tray.IsAdminPromptPending);

        _processes.Running.Clear();
        _watcher.Poll(At(31));
        _dispatcher.Drain();

        Assert.False(_watcher.IsPromptPending);
        Assert.False(_tray.IsAdminPromptPending);
        Assert.Single(_toasts.Shown);
    }

    [Fact]
    public void PromptAfterTheWindowIsIgnored()
    {
        _watcher.Watch(1, "Docker Desktop", At(0));
        _watcher.Poll(At(19.5));

        _processes.Running.Add(AdminPromptWatcher.ConsentProcess);
        _watcher.Poll(At(20.5));
        _watcher.Poll(At(25));
        _dispatcher.Drain();

        Assert.Empty(_toasts.Shown);
        Assert.Equal(0, _flasher.Flashes);
        Assert.False(_tray.IsAdminPromptPending);
    }

    [Fact]
    public void WithoutAWatchedOperationAPromptIsNotOurs()
    {
        _processes.Running.Add(AdminPromptWatcher.ConsentProcess);

        _watcher.Poll(At(1));

        Assert.Empty(_toasts.Shown);
    }

    [Fact]
    public async Task InstallingStageStartsTheWatchOncePerOperation()
    {
        var operation = _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Upgrade);
        for (var i = 0; i < 200 && !_manager.Report("a", new OperationProgress(OperationStage.Downloading, 50)); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, _time.TimersCreated);
        _manager.Report("a", new OperationProgress(OperationStage.Installing, null));
        _manager.Report("a", new OperationProgress(OperationStage.Installing, 50));
        Assert.Equal(1, _time.TimersCreated);

        _processes.Running.Add(AdminPromptWatcher.ConsentProcess);
        _watcher.Poll(At(2));

        Assert.Single(_toasts.Shown);
        _manager.Complete("a", OperationResult.Success());
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
