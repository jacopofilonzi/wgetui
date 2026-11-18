using WgetUi.App.Infrastructure;
using WgetUi.App.Services.Changelog;

namespace WgetUi.App.Tests;

/// <summary>CHANGELOG.md parsing, the embedded copy, and which notes "What's new" shows at startup.</summary>
public sealed class ChangelogTests
{
    private const string Sample = """
        # Changelog

        Intro text that is not part of any version.

        ## [Unreleased]

        ### Added

        - Something not released yet.

        ## [0.3.0] - 2026-11-20

        ### Added

        - **What's new** popup, shown once after an update.
        - Updates download in the background
          and install at the `next start`.
        * Star bullets work too, with [a link](https://example.com/x).

        ### Fixed

        - A fix.

        ### Security

        - An unknown heading is kept as is.

        ## 0.2.0 – 2026-11-18

        - A bullet before any heading.

        A plain paragraph.

        ## [0.1.0]

        ### Removed

        [0.3.0]: https://example.com/compare/v0.2.0...v0.3.0
        """;

    private static readonly IReadOnlyList<ChangelogVersion> Versions = ChangelogParser.Parse(Sample);

    [Fact]
    public void ReadsVersionsNewestFirstAndSkipsUnreleased()
    {
        Assert.Equal(["0.3.0", "0.2.0", "0.1.0"], Versions.Select(v => v.VersionText));
        Assert.Equal("2026-11-20", Versions[0].Date);
        Assert.Equal("2026-11-18", Versions[1].Date);
        Assert.Null(Versions[2].Date);
        Assert.DoesNotContain(Versions.SelectMany(v => v.Sections).SelectMany(s => s.Items), i => i.Contains("not released", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionsKeepTheirHeadingsAndPlainTextBullets()
    {
        var v030 = Versions[0];

        Assert.Equal(["Added", "Fixed", "Security"], v030.Sections.Select(s => s.Heading));
        Assert.Equal(
            [
                "What's new popup, shown once after an update.",
                "Updates download in the background and install at the next start.",
                "Star bullets work too, with a link.",
            ],
            v030.Sections[0].Items);
        Assert.Equal(["An unknown heading is kept as is."], v030.Sections[2].Items);
    }

    [Fact]
    public void ContentWithoutHeadingsAndEmptySectionsAreTolerated()
    {
        var v020 = Versions[1];
        var section = Assert.Single(v020.Sections);
        Assert.False(section.HasHeading);
        Assert.Equal(["A bullet before any heading.", "A plain paragraph."], section.Items);

        // "### Removed" with nothing under it, and a link reference definition.
        Assert.Empty(Versions[2].Sections);
    }

    [Fact]
    public void GarbageGivesNoVersions()
    {
        Assert.Empty(ChangelogParser.Parse(string.Empty));
        Assert.Empty(ChangelogParser.Parse("## [not a version]\n- x\n## [1.2]\n- y"));
    }

    [Fact]
    public void TheEmbeddedChangelogHasNotesForTheRunningVersion()
    {
        // Every release needs a CHANGELOG.md section before it is tagged (the release workflow fails too).
        var text = EmbeddedChangelogSource.ReadText();
        Assert.NotNull(text);
        var current = Assert.Single(ChangelogParser.Parse(text), v => v.Version == AppVersion.Value);
        Assert.True(current.HasDate);
        Assert.NotEmpty(current.Sections.SelectMany(s => s.Items));
    }

    [Fact]
    public void StartupShowsEveryVersionSinceTheLastRunNewestFirst()
    {
        var shown = WhatsNewSelection.AtStartup(Versions, "0.1.0", new Version(0, 3, 0), justUpdated: false);

        Assert.Equal(["0.3.0", "0.2.0"], shown.Select(v => v.VersionText));
    }

    [Fact]
    public void StartupStopsAtTheRunningVersion()
    {
        var shown = WhatsNewSelection.AtStartup(Versions, "0.1.0", new Version(0, 2, 0), justUpdated: true);

        Assert.Equal(["0.2.0"], shown.Select(v => v.VersionText));
    }

    [Theory]
    [InlineData("0.3.0")]
    [InlineData("0.4.0")]
    public void TheSameOrANewerLastRunShowsNothing(string lastRun)
    {
        Assert.Empty(WhatsNewSelection.AtStartup(Versions, lastRun, new Version(0, 3, 0), justUpdated: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    public void WithoutALastRunOnlyASelfUpdateShowsTheCurrentNotes(string? lastRun)
    {
        Assert.Empty(WhatsNewSelection.AtStartup(Versions, lastRun, new Version(0, 3, 0), justUpdated: false));

        var shown = WhatsNewSelection.AtStartup(Versions, lastRun, new Version(0, 3, 0), justUpdated: true);
        Assert.Equal(["0.3.0"], shown.Select(v => v.VersionText));
    }

    [Fact]
    public void SettingsShowsEveryVersionUpToTheRunningOne()
    {
        Assert.Equal(["0.2.0", "0.1.0"], WhatsNewSelection.UpTo(Versions, new Version(0, 2, 0)).Select(v => v.VersionText));
    }
}
