namespace WgetUi.Managers.WinGet;

/// <summary>
/// Plain snapshot of what the details popup needs from COM (catalog metadata of the default install version,
/// the available versions and the installed state), so <see cref="WinGetMapping.ToDetails"/> is tested without COM.
/// </summary>
/// <param name="Description">CatalogPackageMetadata.Description.</param>
/// <param name="ShortDescription">CatalogPackageMetadata.ShortDescription (used when there is no description).</param>
/// <param name="Publisher">CatalogPackageMetadata.Publisher, or the installed ARP publisher for local packages.</param>
/// <param name="PackageUrl">Homepage.</param>
/// <param name="PublisherUrl">Publisher site (homepage fallback).</param>
/// <param name="License">License name (msstore puts a store deep link here).</param>
/// <param name="LicenseUrl">License link.</param>
/// <param name="ReleaseNotesUrl">Release notes link.</param>
/// <param name="InstalledVersion">Installed version ("Unknown"/null when unknown or not installed).</param>
/// <param name="AvailableVersion">Applicable update, or null when none.</param>
/// <param name="Versions">CatalogPackage.AvailableVersions, in winget's order (newest first).</param>
internal sealed record WinGetDetailsData(
    string? Description,
    string? ShortDescription,
    string? Publisher,
    string? PackageUrl,
    string? PublisherUrl,
    string? License,
    string? LicenseUrl,
    string? ReleaseNotesUrl,
    string? InstalledVersion,
    string? AvailableVersion,
    IReadOnlyList<string> Versions);
