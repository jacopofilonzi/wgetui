using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class InstalledViewModelTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new() { ExtraCapabilities = ManagerCapabilities.ListInstalled | ManagerCapabilities.Uninstall | ManagerCapabilities.Details };
    private readonly ManualDispatcher _dispatcher = new();
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly FakeUninstallConfirmer _confirmer = new();
    private readonly RecordingPresenter _presenter = new();
    private readonly AutoUpdateService _autoUpdates;
    private readonly InstalledViewModel _vm;
    private readonly UpdatesViewModel _updates;
    private readonly PackageListOverlay _lists = new();
    private readonly InstalledViewModel _withLists;

    public InstalledViewModelTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        var ignored = new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance);
        var requester = new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);
        _autoUpdates = new AutoUpdateService(_settings, NullLogger<AutoUpdateService>.Instance);
        var launcher = new PackageDetailsLauncher(registry, _presenter, NullLogger<PackageDetailsViewModel>.Instance, _autoUpdates);
        _vm = new InstalledViewModel(registry, actions, launcher, ignored, _autoUpdates, _confirmer, _queue, _dispatcher, NullLogger<InstalledViewModel>.Instance);
        var lists = new PackageListLauncher(registry, _lists, new FakeFilePicker(), new ManualTime(), NullLoggerFactory.Instance);
        _withLists = new InstalledViewModel(registry, actions, launcher, ignored, _autoUpdates, _confirmer, _queue, _dispatcher, NullLogger<InstalledViewModel>.Instance, lists);
        _updates = new UpdatesViewModel(registry, actions, ignored, launcher, _queue, _dispatcher, NullLogger<UpdatesViewModel>.Instance);
        _manager.Installed.AddRange([
            new Package("fake", "main", "7zip.7zip", "7-Zip", "26.05", "26.06"),
            new Package("fake", PackageSource.LocalId, @"ARP\Machine\X64\Tool", "Some Tool", "18.1"),
            new Package("fake", PackageSource.LocalId, "Steam App 1", "A Game", null),
        ]);
        _manager.Upgrades.Add(new Package("fake", "main", "7zip.7zip", "7-Zip", "26.05", "26.06"));
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

    private PackageRowViewModel Row(string id) => _vm.Packages.Single(r => r.Id == id);

    private async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            _dispatcher.Drain();
            if (condition())
            {
                return;
            }

            await Task.Delay(25, Ct);
        }

        Assert.Fail("Condition not reached in time.");
    }

    [Fact]
    public async Task ExportOpensThePopupWithTheLoadedPackagesButTheLocalOnes()
    {
        Assert.False(_vm.ShowListActions);
        Assert.False(_withLists.ExportCommand.CanExecute(null));
        await _withLists.RefreshCommand.ExecuteAsync(null);

        var shown = _withLists.ExportCommand.ExecuteAsync(null);

        var export = Assert.IsType<ExportViewModel>(_lists.Current);
        Assert.Equal(["7zip.7zip"], export.Rows.Select(r => r.Id));
        Assert.Equal("Main", export.Rows[0].SourceName);
        export.CloseCommand.Execute(null);
        await shown;
        Assert.False(_lists.IsOpen);
    }

    [Fact]
    public async Task ListsEveryPackageSortedWithLocalOnesNamedLocal()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["7-Zip", "A Game", "Some Tool"], _vm.Packages.Select(r => r.Name));
        Assert.Equal("Local", Row(@"ARP\Machine\X64\Tool").SourceName);
        Assert.Equal("Main", Row("7zip.7zip").SourceName);
        Assert.All(_vm.Packages, r => Assert.True(r.CanAct && r.CanShowDetails));
    }

    [Fact]
    public async Task PackagesWindowsDoesNotLetUsersUninstallAreHidden()
    {
        _manager.Installed.Add(new Package("fake", "main", "Microsoft.AppInstaller", "App Installer", "1.29") { IsRemovable = false });

        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain(_vm.Packages, r => r.Id == "Microsoft.AppInstaller");
        Assert.Equal(3, _vm.TotalCount);
        _vm.Filter = "installer";
        Assert.Empty(_vm.Packages);
    }

    [Fact]
    public async Task TheFilterMatchesNameOrIdIgnoringCase()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);

        _vm.Filter = "zip";
        Assert.Equal(["7zip.7zip"], _vm.Packages.Select(r => r.Id));
        _vm.Filter = "GAME";
        Assert.Equal(["Steam App 1"], _vm.Packages.Select(r => r.Id));
        _vm.Filter = " ";
        Assert.Equal(3, _vm.Packages.Count);
        Assert.Equal(3, _vm.TotalCount);
    }

    [Fact]
    public async Task DecliningTheConfirmationQueuesNothing()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _confirmer.Answer = false;

        await Row("7zip.7zip").ActCommand.ExecuteAsync(null);

        Assert.Equal(1, _confirmer.Asked);
        Assert.Empty(_queue.Operations);
        Assert.False(Row("7zip.7zip").IsQueued);
    }

    [Fact]
    public async Task ASuccessfulUninstallRemovesTheRowHereAndInUpdates()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        await _updates.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, _updates.Count);

        await Row("7zip.7zip").ActCommand.ExecuteAsync(null);
        var operation = Assert.Single(_queue.Operations);
        Assert.Equal(OperationKind.Uninstall, operation.Kind);
        Assert.True(Row("7zip.7zip").IsQueued);

        _manager.Complete("7zip.7zip", OperationResult.Success());

        await UntilAsync(() => _vm.Packages.Count == 2 && _updates.Count == 0);
        Assert.Equal(2, _vm.TotalCount);
        Assert.Equal(OperationOptions.None, _manager.Received["uninstall:7zip.7zip"]);
    }

    [Fact]
    public async Task AFailedUninstallKeepsTheRowAndEnablesItAgain()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        await Row("Steam App 1").ActCommand.ExecuteAsync(null);

        _manager.Complete("Steam App 1", OperationResult.Failure(ErrorCodes.UninstallFailed, "exit 1"));

        await UntilAsync(() => !Row("Steam App 1").IsQueued);
        Assert.Equal(3, _vm.Packages.Count);
    }

    [Fact]
    public void EnsureLoadedLoadsOnlyOnce()
    {
        _vm.EnsureLoaded();
        _vm.EnsureLoaded();

        Assert.True(_vm.HasLoaded);
        Assert.Equal(3, _vm.Packages.Count);
    }

    [Fact]
    public async Task OnlyPackagesFromASourceGetTheAutomaticUpdateToggle()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(_vm.ShowAutoUpdateFilter);
        Assert.True(Row("7zip.7zip").CanAutoUpdate);
        Assert.False(Row(@"ARP\Machine\X64\Tool").CanAutoUpdate);
        Assert.False(Row("Steam App 1").CanAutoUpdate);
        Assert.Equal("Automatic updates: off", Row("7zip.7zip").AutoUpdateLabel);
    }

    [Fact]
    public async Task TheRowToggleSavesTheRuleAndTheFilterShowsOnlyThoseRows()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);

        Row("7zip.7zip").ToggleAutoUpdateCommand.Execute(null);

        Assert.True(Row("7zip.7zip").IsAutoUpdate);
        Assert.False(Row("7zip.7zip").ShowAutoUpdateForced);
        Assert.Equal("Automatic updates: on", Row("7zip.7zip").AutoUpdateLabel);
        Assert.Equal("7zip.7zip", Assert.Single(_settings.Current.AutoUpdates).PackageId);

        _vm.OnlyAutoUpdates = true;
        Assert.Equal(["7zip.7zip"], _vm.Packages.Select(r => r.Id));

        Row("7zip.7zip").ToggleAutoUpdateCommand.Execute(null);
        Assert.Empty(_vm.Packages);
        Assert.Empty(_settings.Current.AutoUpdates);
    }

    [Fact]
    public async Task TheDetailsSectionSetsAutomaticUpdatesAndForce()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);
        _presenter.OnShow = details =>
        {
            Assert.True(details.ShowAutoUpdate);
            details.AutoUpdate = true;
            details.AutoUpdateForce = true;
            return Task.CompletedTask;
        };

        await Row("7zip.7zip").ShowDetailsCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_settings.Current.AutoUpdates).Force);
        Assert.True(Row("7zip.7zip").ShowAutoUpdateForced);
        Assert.Equal("Automatic updates: on, even if the app is open", Row("7zip.7zip").AutoUpdateLabel);
        Assert.Empty(_queue.Operations);

        _presenter.OnShow = details =>
        {
            Assert.True(details.AutoUpdate && details.AutoUpdateForce);
            details.AutoUpdate = false;
            Assert.False(details.AutoUpdateForce);
            return Task.CompletedTask;
        };
        await Row("7zip.7zip").ShowDetailsCommand.ExecuteAsync(null);

        Assert.Empty(_settings.Current.AutoUpdates);
        Assert.False(Row("7zip.7zip").IsAutoUpdate);
    }

    [Fact]
    public async Task TheDetailsSectionIsHiddenForLocalPackages()
    {
        await _vm.RefreshCommand.ExecuteAsync(null);

        await Row(@"ARP\Machine\X64\Tool").ShowDetailsCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(_presenter.Shown).ShowAutoUpdate);
    }

    [Fact]
    public async Task RulesOfPackagesNoLongerInstalledArePrunedOnRefreshAndOnUninstall()
    {
        _autoUpdates.Set(new Package("fake", "main", "gone.app", "Gone", "1.0"), enabled: true);
        _autoUpdates.Set(_manager.Installed[0], enabled: true);

        await _vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(["7zip.7zip"], _settings.Current.AutoUpdates.Select(r => r.PackageId));

        await Row("7zip.7zip").ActCommand.ExecuteAsync(null);
        _manager.Complete("7zip.7zip", OperationResult.Success());

        await UntilAsync(() => _settings.Current.AutoUpdates.Count == 0);
    }

    private sealed class FakeUninstallConfirmer : IUninstallConfirmer
    {
        public bool Answer { get; set; } = true;

        public int Asked { get; private set; }

        public Task<bool> ConfirmUninstallAsync(Package package)
        {
            Asked++;
            return Task.FromResult(Answer);
        }
    }
}
