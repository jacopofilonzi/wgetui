using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.Tests;

/// <summary>The import popup with fakes: resolution, row states per mode, selection, what gets queued. Nothing real runs.</summary>
public sealed class ImportViewModelTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new() { ExtraCapabilities = ManagerCapabilities.ListInstalled | ManagerCapabilities.FindById | ManagerCapabilities.InstallVersion | ManagerCapabilities.Agreements };
    private readonly ManagerRegistry _registry;
    private readonly OperationQueue _queue;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly AnswerConfirmer _confirmer = new();
    private readonly PackageActions _actions;

    public ImportViewModelTests()
    {
        Loc.Instance.SetLanguage("en");
        _registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(_registry, NullLogger<OperationQueue>.Instance);
        (_settings, _dataDir) = TestSettings.Create();
        var requester = new OperationRequester(_registry, _queue, _confirmer, NullLogger<OperationRequester>.Instance);
        _actions = new PackageActions(requester, _settings, NullLogger<PackageActions>.Instance);

        _manager.Installed.AddRange([
            new Package("fake", "main", "inst.uptodate", "Up To Date", "1.0"),
            new Package("fake", "main", "inst.update", "Has Update", "1.0", "2.0"),
            new Package("fake", "main", "inst.old", "Old One", "1.0", "2.0"),
        ]);
        Offer("new.a", "New A", "3.0", "3.0", "2.0");
        Offer("new.store", "Store App", null);
        Offer("inst.uptodate", "Up To Date", "1.0", "1.0");
        Offer("inst.update", "Has Update", "2.0", "2.0", "1.0");
        Offer("inst.old", "Old One", "2.0", "2.0", "1.0");
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

    private static PackageList List(params PackageListEntry[] entries) => new(entries, new DateTimeOffset(2026, 11, 18, 21, 0, 0, TimeSpan.Zero), "wgetui 0.4.0", PackageListFormat.Wgetui);

    private static PackageListEntry Entry(string id, string? version, string source = "main", string manager = "fake") => new(manager, source, id, id, version);

    private static PackageList Sample() => List(
        Entry("new.a", "2.0"),
        Entry("new.store", "5.0"),
        Entry("inst.update", "1.0"),
        Entry("inst.uptodate", "0.9"),
        Entry("inst.old", "2.0"),
        Entry("missing.x", "1.0"),
        Entry("x", "1.0", source: "other"),
        Entry("y", "1.0", manager: "nomanager"));

    private void Offer(string id, string name, string? latest, params string[] versions) =>
        _manager.Offers[id] = new PackageOffer(new Package("fake", "main", id, name, latest), versions);

    private ImportViewModel Create(PackageList? list = null) =>
        new(list ?? Sample(), "home.wgetui", _registry, _actions, (_, source) => source == "main" ? "Main" : source, NullLogger.Instance);

    private static ImportRowViewModel Row(ImportViewModel vm, string id) => vm.Rows.Single(r => r.Id == id);

    [Fact]
    public async Task LatestModeStatesAndDefaultSelection()
    {
        var vm = Create();
        Assert.All(vm.Rows, r => Assert.Equal(ImportRowState.Checking, r.State));

        await vm.LoadAsync(Ct);

        var a = Row(vm, "new.a");
        Assert.Equal((ImportRowState.ToInstall, true, "New A", "3.0", "pinned 2.0", "To install"), (a.State, a.IsSelected, a.Name, a.MainVersion, a.OtherVersion, a.StatusText));
        Assert.Equal(new ImportRequest(OperationKind.Install, new Package("fake", "main", "new.a", "New A", "3.0"), null), a.Request);

        var store = Row(vm, "new.store");
        Assert.Equal((ImportRowState.ToInstall, true, "Latest"), (store.State, store.IsSelected, store.MainVersion));

        var update = Row(vm, "inst.update");
        Assert.Equal((ImportRowState.Installed, false, true, "Installed 1.0", "2.0"), (update.State, update.IsSelected, update.CanSelect, update.StatusText, update.MainVersion));
        Assert.Equal(OperationKind.Upgrade, update.Request?.Kind);
        Assert.Null(update.Note);

        var upToDate = Row(vm, "inst.uptodate");
        Assert.Equal((ImportRowState.Installed, false, "Up to date", "1.0"), (upToDate.State, upToDate.CanSelect, upToDate.Note, upToDate.MainVersion));

        Assert.Equal((ImportRowState.NotFound, false, "Not found"), (Row(vm, "missing.x").State, Row(vm, "missing.x").CanSelect, Row(vm, "missing.x").StatusText));
        Assert.Equal(ImportRowState.SourceUnavailable, Row(vm, "x").State);
        Assert.Equal(ImportRowState.SourceUnavailable, Row(vm, "y").State);
        Assert.False(Row(vm, "y").CanSelect);

        Assert.Equal(["new.a", "new.store"], vm.SelectedRequests().Select(r => r.Package.Id));
        Assert.Equal("2 of 4 selected", vm.SelectedText);
        Assert.Equal("Install selected (2)", vm.InstallText);
        Assert.Null(vm.SelectAll);
    }

    [Fact]
    public async Task PinnedModeStatesAndRequests()
    {
        var vm = Create();
        await vm.LoadAsync(Ct);

        vm.SetPinnedModeCommand.Execute(null);

        var a = Row(vm, "new.a");
        Assert.Equal((ImportRowState.ToInstall, true, "2.0", "latest 3.0"), (a.State, a.IsSelected, a.MainVersion, a.OtherVersion));
        Assert.Equal("2.0", a.Request?.TargetVersion);

        var store = Row(vm, "new.store");
        Assert.Equal((ImportRowState.VersionNotAvailable, false, false, "Version not available"), (store.State, store.CanSelect, store.IsSelected, store.StatusText));

        var same = Row(vm, "inst.update");
        Assert.Equal((ImportRowState.Installed, false, "Same version"), (same.State, same.CanSelect, same.Note));

        Assert.Equal(ImportRowState.VersionNotAvailable, Row(vm, "inst.uptodate").State);

        var old = Row(vm, "inst.old");
        Assert.Equal((ImportRowState.Installed, true, false), (old.State, old.CanSelect, old.IsSelected));
        Assert.Equal(new ImportRequest(OperationKind.Install, new Package("fake", "main", "inst.old", "Old One", "2.0"), "2.0"), old.Request);

        Assert.Equal(["new.a"], vm.SelectedRequests().Select(r => r.Package.Id));
        Assert.Equal("Installs exactly the version in the list.", vm.ModeHint);

        // Back to latest: the store app is selected again (the user's choice was kept).
        vm.SetLatestModeCommand.Execute(null);
        Assert.True(store.IsSelected);
        Assert.True(vm.IsLatestMode);
    }

    [Fact]
    public async Task WithoutInstallVersionEveryPinnedRowIsUnavailable()
    {
        _manager.ExtraCapabilities = ManagerCapabilities.ListInstalled | ManagerCapabilities.FindById;
        var vm = Create();
        await vm.LoadAsync(Ct);

        vm.SetPinnedModeCommand.Execute(null);

        Assert.Equal(ImportRowState.VersionNotAvailable, Row(vm, "new.a").State);
        Assert.Equal(ImportRowState.VersionNotAvailable, Row(vm, "inst.old").State);
        Assert.Empty(vm.SelectedRequests());
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectAllCoversTheSelectableRows()
    {
        var vm = Create();
        await vm.LoadAsync(Ct);

        vm.SelectAll = true;
        Assert.True(vm.SelectAll);
        Assert.Equal(["new.a", "new.store", "inst.update", "inst.old"], vm.SelectedRequests().Select(r => r.Package.Id));
        Row(vm, "missing.x").IsSelected = true;
        Assert.False(Row(vm, "missing.x").IsSelected);

        vm.SelectAll = false;
        Assert.False(vm.SelectAll);
        Assert.Empty(vm.SelectedRequests());
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task LatestModeQueuesInstallsThenUpgradesAfterClosing()
    {
        var vm = Create();
        await vm.LoadAsync(Ct);
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        Row(vm, "inst.update").IsSelected = true;

        vm.InstallCommand.Execute(null);

        Assert.True(closed);
        Assert.Empty(_queue.Operations);
        await vm.RunChosenActionAsync();
        Assert.Equal(
            [(OperationKind.Install, "new.a", (string?)null), (OperationKind.Install, "new.store", null), (OperationKind.Upgrade, "inst.update", null)],
            _queue.Operations.Select(o => (o.Kind, o.Package.Id, o.Options.TargetVersion)));
    }

    [Fact]
    public async Task PinnedModeQueuesInstallsWithTheirVersions()
    {
        var vm = Create();
        await vm.LoadAsync(Ct);
        vm.SetPinnedModeCommand.Execute(null);
        Row(vm, "inst.old").IsSelected = true;

        vm.InstallCommand.Execute(null);
        await vm.RunChosenActionAsync();

        Assert.Equal(
            [(OperationKind.Install, "new.a", (string?)"2.0"), (OperationKind.Install, "inst.old", "2.0")],
            _queue.Operations.Select(o => (o.Kind, o.Package.Id, o.Options.TargetVersion)));
    }

    [Fact]
    public async Task DecliningTheAgreementsQueuesNothingAtAll()
    {
        _manager.Agreements["new.a"] = p => new PackageAgreements(p, [new Agreement("Terms", "text", null)], []);
        var vm = Create();
        await vm.LoadAsync(Ct);
        Row(vm, "inst.update").IsSelected = true;

        vm.InstallCommand.Execute(null);
        await vm.RunChosenActionAsync();

        Assert.Equal(1, _confirmer.Asked);
        Assert.Empty(_queue.Operations);
    }

    [Fact]
    public async Task ClosingQueuesNothing()
    {
        var vm = Create();
        await vm.LoadAsync(Ct);

        vm.CloseCommand.Execute(null);
        await vm.RunChosenActionAsync();

        Assert.Null(vm.ChosenAction);
        Assert.Empty(_queue.Operations);
    }

    [Fact]
    public async Task AFailedLookupMeansTheSourceIsUnavailable()
    {
        _manager.FailingLookups.Add("new.a");
        _manager.FailingLookups.Add("inst.update");
        var vm = Create();

        await vm.LoadAsync(Ct);

        Assert.Equal(ImportRowState.SourceUnavailable, Row(vm, "new.a").State);
        Assert.Equal(ImportRowState.Installed, Row(vm, "inst.update").State);
        Assert.Equal(OperationKind.Upgrade, Row(vm, "inst.update").Request?.Kind);
    }

    [Fact]
    public async Task ManagersWithoutFindByIdAreAskedForDetails()
    {
        _manager.ExtraCapabilities = ManagerCapabilities.ListInstalled | ManagerCapabilities.Details | ManagerCapabilities.InstallVersion;
        var tool = new Package("fake", "main", "new.a", "New A (details)", null);
        _manager.Details["new.a"] = PackageDetails.Minimal(tool) with { Versions = ["3.1", "3.0"] };
        var vm = Create(List(Entry("new.a", "3.0"), Entry("missing.x", null)));

        await vm.LoadAsync(Ct);

        Assert.Equal(("New A (details)", "3.1", ImportRowState.ToInstall), (Row(vm, "new.a").Name, Row(vm, "new.a").MainVersion, Row(vm, "new.a").State));
        Assert.Equal(ImportRowState.NotFound, Row(vm, "missing.x").State);
    }

    [Fact]
    public void TheSubtitleNamesTheSourceTheCountAndTheDate()
    {
        var vm = Create(List(Entry("new.a", null)));

        Assert.StartsWith("home.wgetui · 1 package · exported on ", vm.Subtitle, StringComparison.Ordinal);
        Assert.Equal("home.wgetui · 8 packages · exported on " + new DateTimeOffset(2026, 11, 18, 21, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("d", Loc.Instance.Culture), Create().Subtitle);
    }

    [Fact]
    public void AFailedListShowsTheErrorAndNothingToInstall()
    {
        var vm = ImportViewModel.Failed("broken.wgetui", ErrorCodes.ListNewerFormat, NullLogger.Instance);

        Assert.True(vm.HasError);
        Assert.False(vm.HasList);
        Assert.Equal("broken.wgetui", vm.Subtitle);
        Assert.Contains(Loc.Instance.Error(ErrorCodes.ListNewerFormat), vm.ErrorText, StringComparison.Ordinal);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    private sealed class AnswerConfirmer : IAgreementsConfirmer
    {
        public int Asked { get; private set; }

        public Task<bool> ConfirmAsync(IReadOnlyList<PackageAgreements> agreements, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(false);
        }
    }
}
