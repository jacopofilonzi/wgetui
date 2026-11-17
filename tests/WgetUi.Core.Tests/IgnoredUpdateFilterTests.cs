using WgetUi.Core.Models;
using WgetUi.Core.Updates;

namespace WgetUi.Core.Tests;

public class IgnoredUpdateFilterTests
{
    private static Package Update(string id, string available, string manager = "fake") => new(manager, "main", id, id, "1.0", available);

    private static IgnoredUpdate Ignored(string id, string version, string manager = "fake") => new(manager, "main", id, version, id);

    [Fact]
    public void TheIgnoredVersionIsHiddenAndItsRecordKept()
    {
        var result = IgnoredUpdateFilter.Apply([Update("a", "2.0"), Update("b", "3.0")], [Ignored("a", "2.0")], isComplete: true);

        Assert.Equal(["b"], result.Visible.Select(p => p.Id));
        Assert.Equal(["a"], result.Ignored.Select(p => p.Id));
        Assert.Equal([Ignored("a", "2.0")], result.Records);
        Assert.False(result.Changed);
    }

    [Fact]
    public void ANewerVersionIsShownAgainAndTheRecordDeleted()
    {
        var result = IgnoredUpdateFilter.Apply([Update("a", "2.1")], [Ignored("a", "2.0")], isComplete: false);

        Assert.Equal(["a"], result.Visible.Select(p => p.Id));
        Assert.Empty(result.Ignored);
        Assert.Empty(result.Records);
        Assert.True(result.Changed);
    }

    [Fact]
    public void RecordsOfPackagesGoneFromTheListAreDroppedOnlyAfterACompleteCheck()
    {
        var complete = IgnoredUpdateFilter.Apply([], [Ignored("gone", "2.0")], isComplete: true);
        var partial = IgnoredUpdateFilter.Apply([], [Ignored("gone", "2.0")], isComplete: false);

        Assert.Empty(complete.Records);
        Assert.True(complete.Changed);
        Assert.Single(partial.Records);
        Assert.False(partial.Changed);
    }

    [Fact]
    public void MatchingIsPerManagerAndSourceAndIgnoresIdCase()
    {
        var result = IgnoredUpdateFilter.Apply(
            [Update("Pkg.A", "2.0"), Update("Pkg.A", "2.0", manager: "other")],
            [Ignored("pkg.a", "2.0")],
            isComplete: true);

        Assert.Equal(["other"], result.Visible.Select(p => p.ManagerId));
        Assert.Equal(["fake"], result.Ignored.Select(p => p.ManagerId));
    }

    [Fact]
    public void DuplicateRecordsCollapse()
    {
        var result = IgnoredUpdateFilter.Apply([Update("a", "2.0")], [Ignored("a", "2.0"), Ignored("a", "2.0")], isComplete: true);

        Assert.Single(result.Records);
        Assert.True(result.Changed);
    }

    [Fact]
    public void ARecordIsOnlyMadeForAPackageWithAnUpdate()
    {
        Assert.Null(IgnoredUpdate.For(new Package("fake", "main", "a", "A", "1.0")));
        Assert.Equal(new IgnoredUpdate("fake", "main", "a", "2.0", "A"), IgnoredUpdate.For(new Package("fake", "main", "a", "A", "1.0", "2.0")));
    }
}
