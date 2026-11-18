using WgetUi.App.Infrastructure;

namespace WgetUi.App.Services.Changelog;

/// <summary>Pure choice of the release notes the "What's new" popup shows at startup.</summary>
public static class WhatsNewSelection
{
    /// <summary>
    /// Every version newer than <paramref name="lastRunVersion"/> up to <paramref name="current"/>, newest first.
    /// Without a readable last-run version (first install, or an upgrade from a version that did not record it)
    /// nothing is shown, unless the app was just restarted by its own update (<c>--updated</c>): then the notes
    /// of the current version. The same or a newer last-run version (a downgrade) shows nothing.
    /// </summary>
    public static IReadOnlyList<ChangelogVersion> AtStartup(IReadOnlyList<ChangelogVersion> versions, string? lastRunVersion, Version current, bool justUpdated)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(current);
        var last = AppVersion.TryParse(lastRunVersion);
        if (last is null)
        {
            return justUpdated ? versions.Where(v => v.Version == current).ToList() : [];
        }

        return versions
            .Where(v => v.Version > last && v.Version <= current)
            .OrderByDescending(v => v.Version)
            .ToList();
    }

    /// <summary>From Settings: every version up to the current one, newest first.</summary>
    public static IReadOnlyList<ChangelogVersion> UpTo(IReadOnlyList<ChangelogVersion> versions, Version current)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions.Where(v => v.Version <= current).OrderByDescending(v => v.Version).ToList();
    }
}
