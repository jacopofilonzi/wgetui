using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class IgnoredUpdatesTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly IgnoredUpdatesService _ignored;
    private readonly UpdatesViewModel _updates;
    private readonly FakeNotifier _toasts = new();
    private readonly UpdateNotifier _notifier;

    public IgnoredUpdatesTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        _settings.Update(s => s.NotifyNewUpdates = true);
        _ignored = new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance);
        var requester = new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);
        _updates = new UpdatesViewModel(registry, actions, _ignored, RecordingPresenter.Launcher(registry), _queue, _dispatcher, NullLogger<UpdatesViewModel>.Instance);
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

    private PackageRowViewModel Row(string id) => _updates.Upgrades.Single(r => r.Id == id);

    [Fact]
    public async Task IgnoringHidesTheRowFromTheListTheCountAndUpgradeAll()
    {
        await RefreshAsync(Pkg("a"), Pkg("b"));

        Row("a").IgnoreCommand.Execute(null);

        Assert.Equal(["b"], _updates.Upgrades.Select(r => r.Id));
        Assert.Equal(1, _updates.Count);
        Assert.Equal(1, _updates.IgnoredCount);
        Assert.True(_updates.ShowIgnoredToggle);
        Assert.Equal([new IgnoredUpdate("fake", "main", "a", "2.0", "a")], _settings.Current.IgnoredUpdates);

        await _updates.UpgradeAllCommand.ExecuteAsync(null);
        Assert.Equal(["b"], _queue.Operations.Select(o => o.Package.Id));
    }

    [Fact]
    public async Task ShowIgnoredListsThemGreyedAndDontIgnoreBringsThemBack()
    {
        await RefreshAsync(Pkg("a"), Pkg("b"));
        Row("a").IgnoreCommand.Execute(null);

        _updates.ShowIgnored = true;
        var row = Row("a");
        Assert.True(row.IsIgnored);
        Assert.False(row.ShowAction);
        Assert.True(row.RowOpacity < 1);
        Assert.Equal(1, _updates.Count);

        row.UnignoreCommand.Execute(null);

        Assert.False(row.IsIgnored);
        Assert.Equal(2, _updates.Count);
        Assert.Empty(_settings.Current.IgnoredUpdates);
    }

    [Fact]
    public async Task IgnoredUpdatesSurviveARefreshAndAreNeverNotified()
    {
        _ignored.Ignore(Pkg("a"));

        await RefreshAsync(Pkg("a"), Pkg("b"));

        Assert.Equal(["b"], _updates.Upgrades.Select(r => r.Id));
        Assert.Single(_toasts.Shown);
        Assert.Equal("b", _toasts.Shown[0].Body);
        Assert.DoesNotContain(_settings.Current.NotifiedUpdates, k => k.Contains("/a@", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANewerVersionIsShownAgainAndTheRecordDeleted()
    {
        _ignored.Ignore(Pkg("a"));

        await RefreshAsync(Pkg("a", "2.1"));

        Assert.Equal(["a"], _updates.Upgrades.Select(r => r.Id));
        Assert.Equal(1, _updates.Count);
        Assert.Empty(_settings.Current.IgnoredUpdates);
    }

    [Fact]
    public async Task SettingsListsIgnoredUpdatesAndRestoreBringsThemBack()
    {
        await RefreshAsync(Pkg("a"), Pkg("b"));
        var settingsVm = new SettingsViewModel(
            _settings,
            new ThemeService(),
            new StartWithWindowsService(new FakeRunKey(), AppPaths.FromArgs(["--data-dir", _dataDir]), @"C:\Apps\wgetui.exe", NullLogger<StartWithWindowsService>.Instance),
            _ignored,
            AppPaths.FromArgs(["--data-dir", _dataDir]));
        Assert.False(settingsVm.HasIgnoredUpdates);

        Row("b").IgnoreCommand.Execute(null);
        var item = Assert.Single(settingsVm.IgnoredUpdates);
        Assert.Equal(("b", "2.0"), (item.PackageId, item.Version));
        Assert.Equal(1, _updates.Count);

        item.RestoreCommand.Execute(null);

        Assert.Empty(settingsVm.IgnoredUpdates);
        Assert.Equal(2, _updates.Count);
    }

    [Fact]
    public void IgnoredRecordsArePersistedInSettingsJson()
    {
        _ignored.Ignore(Pkg("a"));

        var reloaded = new SettingsStore(AppPaths.FromArgs(["--data-dir", _dataDir]), NullLogger<SettingsStore>.Instance).Load();

        Assert.Equal([new IgnoredUpdate("fake", "main", "a", "2.0", "a")], reloaded.IgnoredUpdates);
    }
}
