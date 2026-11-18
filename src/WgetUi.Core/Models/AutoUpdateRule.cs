namespace WgetUi.Core.Models;

/// <summary>
/// An installed package the user wants upgraded automatically when an update is found (any version).
/// </summary>
/// <param name="ManagerId">Owning manager.</param>
/// <param name="SourceId">Source of the package.</param>
/// <param name="PackageId">Manager-specific package id.</param>
/// <param name="Force">Upgrade even while the app is open (the installer may close it).</param>
/// <param name="Name">Display name when it was turned on (logs only, never used to match).</param>
public sealed record AutoUpdateRule(string ManagerId, string SourceId, string PackageId, bool Force = false, string? Name = null)
{
    /// <summary>The rule for <paramref name="package"/>.</summary>
    public static AutoUpdateRule For(Package package, bool force)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new AutoUpdateRule(package.ManagerId, package.SourceId, package.Id, force, package.Name);
    }

    /// <summary>Same package (ids compare case-insensitively, like <see cref="IgnoredUpdate.IsFor"/>).</summary>
    public bool IsFor(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        // Same identity as Package.Key. Not a property: this record is persisted as is (settings.json).
        return string.Equals($"{ManagerId}/{SourceId}/{PackageId}", package.Key, StringComparison.OrdinalIgnoreCase);
    }
}
