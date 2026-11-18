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
    private readonly InstalledViewModel _vm;
    private readonly UpdatesViewModel _updates;

    public InstalledViewModelTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        var ignored = new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance);
        var requester = new OperationRequester(registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);
        var launcher = RecordingPresenter.Launcher(registry);
        _vm = new InstalledViewModel(registry, actions, launcher, ignored, _confirmer, _queue, _dispatcher, NullLogger<InstalledViewModel>.Instance);
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
