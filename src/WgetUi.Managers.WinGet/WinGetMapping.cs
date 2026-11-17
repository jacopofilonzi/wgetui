using Microsoft.Management.Deployment;
using WgetUi.Core.Models;

namespace WgetUi.Managers.WinGet;

/// <summary>
/// Pure mapping between winget COM values and Core models: packages, statuses and progress.
/// Kept free of COM calls so it is unit-tested without the winget server.
/// </summary>
internal static class WinGetMapping
{
    public const string ManagerId = "winget";
    public const string WinGetSourceId = "winget";
    public const string MsStoreSourceId = "msstore";

    /// <summary>The sources this manager exposes, keyed by winget catalog name.</summary>
    public static IReadOnlyList<PackageSource> Sources { get; } =
    [
        new PackageSource(ManagerId, WinGetSourceId, "winget"),
        new PackageSource(ManagerId, MsStoreSourceId, "Microsoft Store"),
    ];

    /// <summary>winget reports versions it cannot read as "Unknown" (or nothing).</summary>
    public static bool IsUnknownVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) || string.Equals(version, "Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>Maps a winget catalog name to one of our source ids, or null for sources we do not expose.</summary>
    public static string? ToSourceId(string? catalogName) => catalogName switch
    {
        _ when string.Equals(catalogName, WinGetSourceId, StringComparison.OrdinalIgnoreCase) => WinGetSourceId,
        _ when string.Equals(catalogName, MsStoreSourceId, StringComparison.OrdinalIgnoreCase) => MsStoreSourceId,
        _ => null,
    };

    /// <summary>A search hit; <paramref name="sourceId"/> is the catalog that was searched.</summary>
    public static Package? ToSearchResult(WinGetPackageData data, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(data.Id))
        {
            return null;
        }

        var version = IsUnknownVersion(data.AvailableVersion) ? null : data.AvailableVersion;
        return new Package(ManagerId, sourceId, data.Id, NameOrId(data), version);
    }

    /// <summary>An installed package with an applicable upgrade, or null when it must not be listed.</summary>
    public static Package? ToUpgrade(WinGetPackageData data, bool includeUnknown)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.IsUpdateAvailable || string.IsNullOrWhiteSpace(data.Id) || IsUnknownVersion(data.AvailableVersion))
        {
            return null;
        }

        var sourceId = ToSourceId(data.SourceName);
        if (sourceId is null)
        {
            return null;
        }

        var unknownInstalled = IsUnknownVersion(data.InstalledVersion);
        if (unknownInstalled && !includeUnknown)
        {
            return null;
        }

        return new Package(ManagerId, sourceId, data.Id, NameOrId(data), unknownInstalled ? null : data.InstalledVersion, data.AvailableVersion);
    }

    /// <summary>Drops repeated packages (same manager, source and id), keeping the first.</summary>
    public static IReadOnlyList<Package> Distinct(IEnumerable<Package> packages) =>
        [.. packages.DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase)];

    public static OperationProgress ToProgress(PackageInstallProgressState state, double downloadProgress, double installationProgress) => state switch
    {
        PackageInstallProgressState.Queued => new OperationProgress(OperationStage.Preparing, null),
        PackageInstallProgressState.Downloading => new OperationProgress(OperationStage.Downloading, Percent(downloadProgress)),
        // Most installers report no progress: 0 means "unknown", not "just started".
        PackageInstallProgressState.Installing => new OperationProgress(OperationStage.Installing, installationProgress > 0 ? Percent(installationProgress) : null),
        PackageInstallProgressState.PostInstall => new OperationProgress(OperationStage.Finalizing, null),
        PackageInstallProgressState.Finished => new OperationProgress(OperationStage.Finalizing, 100),
        _ => new OperationProgress(OperationStage.Preparing, null),
    };

    /// <summary>Maps the result of an install/upgrade to an <see cref="OperationResult"/>.</summary>
    /// <param name="status">winget result status.</param>
    /// <param name="extendedErrorCode">HRESULT from <c>InstallResult.ExtendedErrorCode</c> (0 when none).</param>
    /// <param name="installerErrorCode">Installer exit code.</param>
    /// <param name="rebootRequired">Installer asked for a reboot.</param>
    /// <param name="what">"install 7zip.7zip" etc., for the log message.</param>
    public static OperationResult ToResult(InstallResultStatus status, int extendedErrorCode, uint installerErrorCode, bool rebootRequired, string what)
    {
        if (status == InstallResultStatus.Ok)
        {
            return OperationResult.Success(rebootRequired);
        }

        var code = status switch
        {
            InstallResultStatus.BlockedByPolicy => ErrorCodes.BlockedByPolicy,
            InstallResultStatus.CatalogError => ErrorCodes.CatalogError,
            InstallResultStatus.InvalidOptions => ErrorCodes.InvalidOptions,
            InstallResultStatus.DownloadError => ErrorCodes.DownloadFailed,
            InstallResultStatus.InstallError => ErrorCodes.InstallFailed,
            InstallResultStatus.ManifestError => ErrorCodes.ManifestError,
            InstallResultStatus.NoApplicableInstallers => ErrorCodes.NoApplicableInstaller,
            InstallResultStatus.NoApplicableUpgrade => ErrorCodes.NoApplicableUpgrade,
            InstallResultStatus.PackageAgreementsNotAccepted => ErrorCodes.AgreementsNotAccepted,
            _ => ErrorCodes.InternalError,
        };

        return OperationResult.Failure(code, $"winget {what} failed: {status} (HRESULT 0x{extendedErrorCode:X8}, installer exit code {installerErrorCode}).");
    }

    public static string ToErrorCode(FindPackagesResultStatus status) => status switch
    {
        FindPackagesResultStatus.BlockedByPolicy => ErrorCodes.BlockedByPolicy,
        FindPackagesResultStatus.CatalogError or FindPackagesResultStatus.AuthenticationError or FindPackagesResultStatus.AccessDenied => ErrorCodes.CatalogError,
        FindPackagesResultStatus.InvalidOptions => ErrorCodes.InvalidOptions,
        _ => ErrorCodes.InternalError,
    };

    /// <summary>Maps an agreement; entries with neither label, text nor link are dropped.</summary>
    public static Agreement? ToAgreement(string? label, string? text, string? url)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var cleanLabel = Clean(label);
        var cleanText = Clean(text);
        var cleanUrl = Clean(url);
        if (cleanLabel is null && cleanText is null && cleanUrl is null)
        {
            return null;
        }

        return new Agreement(cleanLabel ?? cleanUrl ?? string.Empty, cleanText, cleanUrl);
    }

    /// <summary>
    /// License text for display, or null. msstore puts a store deep link (ms-windows-store://…) there, which is
    /// not a license name; a non-http link is dropped as well.
    /// </summary>
    public static (string? License, string? Url) ToLicense(string? license, string? url)
    {
        var cleanLicense = string.IsNullOrWhiteSpace(license) || license.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase) ? null : license.Trim();
        var cleanUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;
        return (cleanLicense, cleanLicense is null ? null : cleanUrl);
    }

    private static double Percent(double fraction) => Math.Clamp(fraction, 0, 1) * 100;

    private static string NameOrId(WinGetPackageData data) => string.IsNullOrWhiteSpace(data.Name) ? data.Id : data.Name;
}
