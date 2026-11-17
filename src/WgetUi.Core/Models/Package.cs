namespace WgetUi.Core.Models;

/// <summary>
/// A package as seen by one manager and source. For search results <see cref="Version"/> is the latest
/// available version; for upgrades it is the installed version and <see cref="AvailableVersion"/> the new one.
/// </summary>
/// <param name="ManagerId">Id of the <see cref="Managers.IPackageManager"/> that owns the package.</param>
/// <param name="SourceId">Id of the <see cref="PackageSource"/> (e.g. "winget", "msstore").</param>
/// <param name="Id">Manager-specific package id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Version">Version, or null when the manager cannot tell (unknown installed version).</param>
/// <param name="AvailableVersion">Newer version available, only for upgrades.</param>
public sealed record Package(
    string ManagerId,
    string SourceId,
    string Id,
    string Name,
    string? Version,
    string? AvailableVersion = null)
{
    /// <summary>Identity across managers and sources, stable for de-duplication.</summary>
    public string Key => $"{ManagerId}/{SourceId}/{Id}";

    /// <summary>True when the installed version is not known to the manager.</summary>
    public bool IsVersionUnknown => string.IsNullOrEmpty(Version);
}
