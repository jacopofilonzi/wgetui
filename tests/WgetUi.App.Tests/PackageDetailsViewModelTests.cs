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

public sealed class PackageDetailsViewModelTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ScriptedManager _manager = new() { ExtraCapabilities = ManagerCapabilities.Details | ManagerCapabilities.InstallVersion | ManagerCapabilities.Uninstall | ManagerCapabilities.Search };
    private readonly ManagerRegistry _registry;
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly RecordingPresenter _presenter = new();
    private readonly IgnoredUpdatesService _ignored;
    private readonly UpdatesViewModel _updates;
    private readonly SearchViewModel _search;

    public PackageDetailsViewModelTests()
    {
        _registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(_registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        _settings.Update(s => s.AutoAcceptAgreements = true);
        _ignored = new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance);
        var requester = new OperationRequester(_registry, _queue, new NoAgreements(), NullLogger<OperationRequester>.Instance);
        var actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);
        var launcher = RecordingPresenter.Launcher(_registry, _presenter);
        var dispatcher = new ManualDispatcher();
        _updates = new UpdatesViewModel(_registry, actions, _ignored, launcher, _queue, dispatcher, NullLogger<UpdatesViewModel>.Instance);
        _search = new SearchViewModel(_registry, actions, launcher, _queue, dispatcher);
        _manager.Details["a"] = Details(ScriptedManager.Package("a"), installed: "1.0", available: "2.0", "2.0", "1.5", "1.0");
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

    private static PackageDetails Details(Package package, string? installed, string? available, params string[] versions) =>
        new(package, "An archiver.", "Igor Pavlov", "https://www.7-zip.org/", "LGPL-2.1", "https://7-zip.org/license.txt", null, installed, available, versions);

    private PackageDetailsViewModel Create(Package package, DetailsContext context, PackageDetailsActions actions) =>
        new(package, "Main", context, _registry, actions, NullLogger.Instance);

    [Fact]
    public async Task LoadedDetailsFillTheCardAndTheVersionList()
    {
        var vm = Create(ScriptedManager.Package("a"), DetailsContext.Updates, new PackageDetailsActions(_ => Task.CompletedTask, _ => { }, (_, _) => Task.CompletedTask));

        await vm.LoadAsync(Ct);

        Assert.Equal("An archiver.", vm.Description);
        Assert.Equal("7-zip.org", vm.HomepageText);
        Assert.True(vm.HasLicenseLink);
        Assert.False(vm.HasReleaseNotes);
        Assert.Equal(["2.0", "1.5", "1.0"], vm.Versions.Select(v => v.Version));
        Assert.True(vm.Versions[0].IsLatest);
        Assert.True(vm.Versions[2].IsInstalled);
        Assert.Equal([true, true, false], vm.Versions.Select(v => v.CanInstall));
        Assert.Equal("Upgrade to 2.0", vm.PrimaryText);
        Assert.Equal("Ignore 2.0", vm.IgnoreText);
        Assert.True(vm.ShowIgnore);
    }

    [Fact]
    public async Task AFailedLoadKeepsTheBasicsAndShowsAnError()
    {
        var vm = Create(ScriptedManager.Package("unknown"), DetailsContext.Updates, new PackageDetailsActions(_ => Task.CompletedTask));

        await vm.LoadAsync(Ct);

        Assert.True(vm.HasError);
        Assert.Equal("unknown", vm.Name);
        Assert.Equal("2.0", vm.AvailableVersion);
        Assert.Empty(vm.Versions);
    }

    [Theory]
    [InlineData(DetailsContext.Search, null, null, "Install")]
    [InlineData(DetailsContext.Search, "1.0", null, null)]
    [InlineData(DetailsContext.Updates, "1.0", "2.0", "Upgrade to 2.0")]
    [InlineData(DetailsContext.Updates, "2.0", null, null)]
    [InlineData(DetailsContext.Installed, "1.0", null, "Uninstall")]
    public async Task ThePrimaryActionFollowsThePageAndTheState(DetailsContext context, string? installed, string? available, string? expected)
    {
        _manager.Details["p"] = Details(new Package("fake", "main", "p", "P", installed), installed, available, "2.0");
        var vm = Create(new Package("fake", "main", "p", "P", installed, available), context, new PackageDetailsActions(_ => Task.CompletedTask, _ => { }));

        await vm.LoadAsync(Ct);

        Assert.Equal(expected, vm.PrimaryText);
        Assert.Equal(available is not null, vm.ShowIgnore);
    }

    [Fact]
    public async Task WithoutTheInstallVersionCapabilityNoVersionCanBeInstalled()
    {
        var plain = new ScriptedManager { ExtraCapabilities = ManagerCapabilities.Details };
        plain.Details["a"] = Details(ScriptedManager.Package("a"), "1.0", "2.0", "2.0", "1.0");
        var registry = new ManagerRegistry([plain], NullLogger<ManagerRegistry>.Instance);
        var vm = new PackageDetailsViewModel(ScriptedManager.Package("a"), "Main", DetailsContext.Updates, registry, new PackageDetailsActions(InstallVersion: (_, _) => Task.CompletedTask), NullLogger.Instance);

        await vm.LoadAsync(Ct);

        Assert.All(vm.Versions, v => Assert.False(v.CanInstall));
        Assert.Null(vm.PrimaryText);
        Assert.False(vm.ShowIgnore);
    }

    [Fact]
    public async Task AnActionClosesThePopupFirstAndRunsAfterwards()
    {
        var ran = false;
        var vm = Create(ScriptedManager.Package("a"), DetailsContext.Updates, new PackageDetailsActions(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }));
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        await vm.LoadAsync(Ct);

        vm.PrimaryCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.False(ran);
        await vm.RunChosenActionAsync();
        Assert.True(ran);
    }

    [Fact]
    public async Task CloseRunsNothing()
    {
        var ran = false;
        var vm = Create(ScriptedManager.Package("a"), DetailsContext.Updates, new PackageDetailsActions(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }));

        vm.CloseCommand.Execute(null);
        await vm.RunChosenActionAsync();

        Assert.False(ran);
    }

    [Fact]
    public async Task UpdatesRowOpensDetailsAndIgnoreFromThePopupHidesTheUpdate()
    {
        _manager.Upgrades.Add(ScriptedManager.Package("a"));
        await _updates.RefreshCommand.ExecuteAsync(null);
        _presenter.OnShow = d =>
        {
            d.IgnoreCommand.Execute(null);
            return Task.CompletedTask;
        };

        await Assert.Single(_updates.Upgrades).ShowDetailsCommand.ExecuteAsync(null);

        Assert.Equal(DetailsContext.Updates, Assert.Single(_presenter.Shown).Context);
        Assert.Equal(0, _updates.Count);
        Assert.Single(_ignored.Records);
    }

    [Fact]
    public async Task UpgradeFromThePopupQueuesTheUpgrade()
    {
        _manager.Upgrades.Add(ScriptedManager.Package("a"));
        await _updates.RefreshCommand.ExecuteAsync(null);
        _presenter.OnShow = d =>
        {
            d.PrimaryCommand.Execute(null);
            return Task.CompletedTask;
        };

        await Assert.Single(_updates.Upgrades).ShowDetailsCommand.ExecuteAsync(null);

        var operation = Assert.Single(_queue.Operations);
        Assert.Equal(OperationKind.Upgrade, operation.Kind);
        Assert.True(_updates.Upgrades[0].IsQueued);
    }

    [Fact]
    public async Task InstallThisVersionFromSearchQueuesAnInstallOfThatVersion()
    {
        var hit = new Package("fake", "main", "a", "A", "2.0");
        _manager.SearchResults.Add(hit);
        _manager.Details["a"] = Details(hit, installed: null, available: "2.0", "2.0", "1.5");
        _search.Query = "a";
        await _search.SearchCommand.ExecuteAsync(null);
        _presenter.OnShow = d =>
        {
            Assert.Equal("Install", d.PrimaryText);
            d.Versions[1].InstallCommand.Execute(null);
            return Task.CompletedTask;
        };

        await Assert.Single(_search.Results).ShowDetailsCommand.ExecuteAsync(null);

        var operation = Assert.Single(_queue.Operations);
        Assert.Equal((OperationKind.Install, "1.5"), (operation.Kind, operation.Options.TargetVersion));
        _manager.Complete("a", OperationResult.Success());
        await operation.Completion.WaitAsync(Timeout, Ct);
        Assert.Equal("1.5", _manager.Received["install:a"].TargetVersion);
    }
}
