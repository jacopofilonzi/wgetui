using WgetUi.Core.Models;

namespace WgetUi.Core.Updates;

/// <summary>Outcome of <see cref="IgnoredUpdateFilter.Apply"/>.</summary>
/// <param name="Visible">Updates to count, notify and offer in "upgrade all".</param>
/// <param name="Ignored">Updates hidden because the user ignored that exact version.</param>
/// <param name="Records">The records to keep (stale ones dropped).</param>
/// <param name="Changed">True when <paramref name="Records"/> differs from the input and must be saved.</param>
public sealed record IgnoredUpdateResult(IReadOnlyList<Package> Visible, IReadOnlyList<Package> Ignored, IReadOnlyList<IgnoredUpdate> Records, bool Changed);

/// <summary>
/// Applies the "ignore this version" records to a freshly listed set of updates. Pure, manager-agnostic.
/// </summary>
/// <remarks>
/// Rules:
/// - an update whose available version equals an ignored record is hidden;
/// - an update of the same package with any other version (managers only offer the latest applicable one, so in
///   practice a newer version) is shown again and its record is deleted;
/// - after a complete check (no manager failed), records whose package is not in the list any more are deleted
///   too: the package was upgraded, uninstalled or caught up. After a partial check they are kept, since a failing
///   manager returned nothing.
/// So the stored list never grows past the number of pending updates.
/// </remarks>
public static class IgnoredUpdateFilter
{
    public static IgnoredUpdateResult Apply(IReadOnlyList<Package> updates, IReadOnlyList<IgnoredUpdate> records, bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(records);
        var visible = new List<Package>(updates.Count);
        var ignored = new List<Package>();
        foreach (var update in updates)
        {
            (records.Any(r => r.Matches(update)) ? ignored : visible).Add(update);
        }

        var kept = records
            .Where(r =>
            {
                var current = updates.FirstOrDefault(r.IsFor);
                return current is null ? !isComplete : r.Matches(current);
            })
            .Distinct()
            .ToList();
        return new IgnoredUpdateResult(visible, ignored, kept, kept.Count != records.Count);
    }
}
