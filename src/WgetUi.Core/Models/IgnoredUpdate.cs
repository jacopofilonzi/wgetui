namespace WgetUi.Core.Models;

/// <summary>An update the user chose to ignore: one exact available version of one package.</summary>
/// <param name="ManagerId">Owning manager.</param>
/// <param name="SourceId">Source of the package.</param>
/// <param name="PackageId">Manager-specific package id.</param>
/// <param name="Version">The ignored available version.</param>
/// <param name="Name">Display name when it was ignored (for the Settings list only, never used to match).</param>
public sealed record IgnoredUpdate(string ManagerId, string SourceId, string PackageId, string Version, string? Name = null)
{
    /// <summary>Same identity as <see cref="Package.Key"/>.</summary>
    public string PackageKey => $"{ManagerId}/{SourceId}/{PackageId}";

    /// <summary>The record for the update <paramref name="package"/> offers, or null when it offers none.</summary>
    public static IgnoredUpdate? For(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return string.IsNullOrEmpty(package.AvailableVersion)
            ? null
            : new IgnoredUpdate(package.ManagerId, package.SourceId, package.Id, package.AvailableVersion, package.Name);
    }

    /// <summary>Same package (ids compare case-insensitively, like the managers' de-duplication).</summary>
    public bool IsFor(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return string.Equals(PackageKey, package.Key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Same package and the very version that was ignored.</summary>
    public bool Matches(Package package) => IsFor(package) && string.Equals(Version, package.AvailableVersion, StringComparison.OrdinalIgnoreCase);
}
