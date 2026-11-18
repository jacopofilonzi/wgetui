using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Services.AppUpdates;
using WgetUi.App.Services.Changelog;
using WgetUi.App.ViewModels;

namespace WgetUi.App.Tests;

/// <summary>When the "What's new" popup opens, and what it holds (scripted notes, a scratch settings file).</summary>
public sealed class WhatsNewTests : IDisposable
{
    private static readonly IReadOnlyList<ChangelogVersion> Notes = ChangelogParser.Parse("""
        ## [0.3.0] - 2026-11-20
        ### Added
        - Three.
        ## [0.2.1] - 2026-11-19
        ### Fixed
        - Two point one.
        ## [0.2.0] - 2026-11-18
        ### Added
        - Two.
        """);

    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly WhatsNewOverlay _overlay = new();

    public WhatsNewTests()
    {
        (_settings, _dataDir) = TestSettings.Create();
        Loc.Instance.SetLanguage("en");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    /// <summary>A launcher on scripted notes, running <paramref name="version"/>.</summary>
    internal static WhatsNewLauncher Launcher(SettingsService settings, WhatsNewOverlay overlay, string version = "0.3.0", bool justUpdated = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wgetui-tests", "unused");
        string[] args = justUpdated ? ["--data-dir", dir, AppPaths.UpdatedArgument] : ["--data-dir", dir];
        return new WhatsNewLauncher(
            settings,
            new FakeChangelog(Notes),
            new AppUpdateOptions(Version.Parse(version), dir, dir),
            AppPaths.FromArgs(args),
            overlay,
            NullLogger<WhatsNewLauncher>.Instance);
    }

    [Fact]
    public void AfterAnUpdateTheNotesSinceTheLastRunOpenOnceWhenTheWindowIsShown()
    {
        _settings.Update(s => s.LastRunVersion = "0.2.0");
        var launcher = Launcher(_settings, _overlay);

        launcher.RecordStartup();

        // Recorded before any window, so a run that never opens it doesn't show them again.
        Assert.Equal("0.3.0", _settings.Current.LastRunVersion);
        Assert.True(launcher.HasPending);
        Assert.False(_overlay.IsOpen);

        launcher.ShowPending();
        var shown = Assert.IsType<WhatsNewViewModel>(_overlay.Current);
        Assert.Equal("What's new in wgetui 0.3.0", shown.Title);
        Assert.Equal(["0.3.0", "0.2.1"], shown.Versions.Select(v => v.VersionText));
        Assert.Equal(new Uri("https://github.com/jacopofilonzi/wgetui/releases/tag/v0.3.0"), shown.ReleasePage);

        shown.CloseCommand.Execute(null);
        Assert.False(_overlay.IsOpen);

        launcher.ShowPending();
        Assert.False(_overlay.IsOpen);

        // Next start of the same version: nothing.
        var next = Launcher(_settings, _overlay);
        next.RecordStartup();
        Assert.False(next.HasPending);
    }

    [Fact]
    public void AFirstInstallRecordsTheVersionAndShowsNothing()
    {
        var launcher = Launcher(_settings, _overlay);

        launcher.RecordStartup();
        launcher.ShowPending();

        Assert.False(_overlay.IsOpen);
        Assert.Equal("0.3.0", _settings.Current.LastRunVersion);
    }

    [Fact]
    public void WithoutALastRunASelfUpdateRestartShowsTheCurrentNotes()
    {
        var launcher = Launcher(_settings, _overlay, justUpdated: true);

        launcher.RecordStartup();
        launcher.ShowPending();

        var shown = Assert.IsType<WhatsNewViewModel>(_overlay.Current);
        Assert.Equal(["0.3.0"], shown.Versions.Select(v => v.VersionText));
    }

    [Fact]
    public void SettingsShowsEveryVersionUpToTheRunningOne()
    {
        var launcher = Launcher(_settings, _overlay, version: "0.2.1");

        launcher.ShowAll();

        var shown = Assert.IsType<WhatsNewViewModel>(_overlay.Current);
        Assert.Equal("What's new in wgetui 0.2.1", shown.Title);
        Assert.Equal(["0.2.1", "0.2.0"], shown.Versions.Select(v => v.VersionText));
        Assert.True(shown.HasVersions);
    }

    [Fact]
    public void TheUpdatedFlagIsParsedAndChangesNothingElse()
    {
        var paths = AppPaths.FromArgs([AppPaths.TrayArgument, AppPaths.UpdatedArgument]);

        Assert.True(paths.JustUpdated);
        Assert.True(paths.StartInTray);
        Assert.False(AppPaths.FromArgs([AppPaths.TrayArgument]).JustUpdated);
    }

    private sealed class FakeChangelog(IReadOnlyList<ChangelogVersion> versions) : IChangelogSource
    {
        public IReadOnlyList<ChangelogVersion> Versions { get; } = versions;
    }
}
