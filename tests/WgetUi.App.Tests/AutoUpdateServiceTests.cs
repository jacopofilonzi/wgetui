using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.App.Tests;

public sealed class AutoUpdateServiceTests : IDisposable
{
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly AutoUpdateService _service;

    public AutoUpdateServiceTests()
    {
        (_settings, _dataDir) = TestSettings.Create();
        _service = new AutoUpdateService(_settings, NullLogger<AutoUpdateService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static Package Pkg(string id, string source = "main") => new("fake", source, id, id.ToUpperInvariant(), "1.0");

    private SettingsService Reload()
    {
        var store = new SettingsStore(AppPaths.FromArgs(["--data-dir", _dataDir]), NullLogger<SettingsStore>.Instance);
        return new SettingsService(store);
    }

    [Fact]
    public void DefaultsKeepTheOldBehaviour()
    {
        var settings = new AppSettings();

        Assert.Empty(settings.AutoUpdates);
        Assert.True(settings.AutoUpdatesEnabled);
        Assert.False(settings.AutoUpdateOpenApps);
        Assert.Empty(settings.PostponedUpdates);
    }

    [Fact]
    public void OldSettingsFilesLoadWithTheDefaults()
    {
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"), """{ "language": "it", "closeToTray": false }""");

        var loaded = Reload().Current;

        Assert.Equal("it", loaded.Language);
        Assert.Empty(loaded.AutoUpdates);
        Assert.True(loaded.AutoUpdatesEnabled);
    }

    [Fact]
    public void RulesAreSavedAndMatchCaseInsensitively()
    {
        var changes = 0;
        _service.Changed += (_, _) => changes++;

        Assert.True(_service.Set(Pkg("a"), enabled: true, force: true));

        var rule = Assert.Single(Reload().Current.AutoUpdates);
        Assert.Equal(new AutoUpdateRule("fake", "main", "a", Force: true, Name: "A"), rule);
        Assert.True(_service.IsEnabled(new Package("fake", "main", "A", "A", "1.0")));
        Assert.True(_service.IsForced(Pkg("a")));
        Assert.False(_service.IsEnabled(Pkg("a", source: "other")));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ToggleTurnsOnWithoutForceAndOffAgain()
    {
        _service.Toggle(Pkg("a"));
        Assert.True(_service.IsEnabled(Pkg("a")));
        Assert.False(_service.IsForced(Pkg("a")));

        _service.Set(Pkg("a"), enabled: true, force: true);
        Assert.Single(_service.Rules);

        _service.Toggle(Pkg("a"));
        Assert.Empty(_service.Rules);
    }

    [Fact]
    public void WillHandleNeedsTheRuleAndTheGlobalSwitch()
    {
        _service.Set(Pkg("a"), enabled: true);
        Assert.True(_service.WillHandle(Pkg("a")));
        Assert.False(_service.WillHandle(Pkg("b")));

        _settings.Update(s => s.AutoUpdatesEnabled = false);

        Assert.False(_service.WillHandle(Pkg("a")));
        Assert.True(_service.IsEnabled(Pkg("a")));
    }

    [Fact]
    public void OnlyManagersThatListedTheirPackagesPruneRules()
    {
        _service.Set(Pkg("a"), enabled: true);
        _service.Set(Pkg("b"), enabled: true);

        _service.PruneInstalled([Pkg("a")], listedManagers: []);
        Assert.Equal(2, _service.Rules.Count);

        _service.PruneInstalled([Pkg("a")], listedManagers: ["fake"]);
        Assert.Equal(["a"], _service.Rules.Select(r => r.PackageId));

        _service.Remove(Pkg("a"));
        Assert.Empty(_service.Rules);
    }

    [Fact]
    public void SettingsTogglesSaveTheGlobalSwitches()
    {
        var paths = AppPaths.FromArgs(["--data-dir", _dataDir]);
        var vm = new WgetUi.App.ViewModels.SettingsViewModel(
            _settings,
            new ThemeService(),
            new StartWithWindowsService(new FakeRunKey(), paths, @"C:\Apps\wgetui.exe", NullLogger<StartWithWindowsService>.Instance),
            new IgnoredUpdatesService(_settings, NullLogger<IgnoredUpdatesService>.Instance),
            paths);
        Assert.True(vm.AutoUpdatesEnabled);
        Assert.False(vm.AutoUpdateOpenApps);

        vm.AutoUpdatesEnabled = false;
        vm.AutoUpdateOpenApps = true;

        var saved = Reload().Current;
        Assert.False(saved.AutoUpdatesEnabled);
        Assert.True(saved.AutoUpdateOpenApps);
        Assert.False(_service.IsGloballyEnabled);
        Assert.True(_service.ForceOpenApps);
    }

    [Fact]
    public void LocalPackagesAndManagersThatCannotUpgradeGetNoRule()
    {
        Assert.True(AutoUpdateService.CanAutoUpdate(Pkg("a"), ManagerCapabilities.Upgrade));
        Assert.False(AutoUpdateService.CanAutoUpdate(Pkg("a", PackageSource.LocalId), ManagerCapabilities.Upgrade));
        Assert.False(AutoUpdateService.CanAutoUpdate(Pkg("a"), ManagerCapabilities.Install | ManagerCapabilities.ListInstalled));
    }
}
