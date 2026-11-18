namespace WgetUi.Managers.WinGet;

/// <summary>
/// Plain snapshot of the fields read from a COM <c>CatalogPackage</c>, so the mapping to Core models can be
/// tested without COM.
/// </summary>
/// <param name="Id">winget package id.</param>
/// <param name="Name">Display name.</param>
/// <param name="SourceName">Catalog name of the version that would be installed ("winget", "msstore"), or null.</param>
/// <param name="InstalledVersion">Installed version as reported by winget ("Unknown" or null when unknown).</param>
/// <param name="AvailableVersion">Version that would be installed (DefaultInstallVersion).</param>
/// <param name="IsUpdateAvailable">winget's own "a newer version applies" flag.</param>
/// <param name="PackageFamilyNames">MSIX package family names of the installed version (installed list only;
/// set also for correlated packages such as App Installer listed as <c>Microsoft.AppInstaller</c>).</param>
internal sealed record WinGetPackageData(
    string Id,
    string Name,
    string? SourceName,
    string? InstalledVersion,
    string? AvailableVersion,
    bool IsUpdateAvailable,
    IReadOnlyList<string>? PackageFamilyNames = null);
