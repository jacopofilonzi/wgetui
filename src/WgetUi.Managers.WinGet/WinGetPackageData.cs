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
internal sealed record WinGetPackageData(
    string Id,
    string Name,
    string? SourceName,
    string? InstalledVersion,
    string? AvailableVersion,
    bool IsUpdateAvailable);
