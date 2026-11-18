namespace WgetUi.Core.Models;

/// <summary>
/// Stable machine-readable error codes. The UI maps each code to its own translated text;
/// the English messages that travel with them are for logs only. Never rename a code.
/// </summary>
public static class ErrorCodes
{
    public const string Cancelled = "cancelled";
    public const string Unexpected = "unexpected";
    public const string Timeout = "timeout";
    public const string ManagerNotFound = "manager_not_found";
    public const string ManagerUnavailable = "manager_unavailable";
    public const string NotSupported = "not_supported";
    public const string ComActivationFailed = "com_activation_failed";
    public const string ComActivationElevated = "com_activation_elevated";
    public const string CatalogError = "catalog_error";
    public const string PackageNotFound = "package_not_found";
    public const string BlockedByPolicy = "blocked_by_policy";
    public const string InvalidOptions = "invalid_options";
    public const string DownloadFailed = "download_failed";
    public const string InstallFailed = "install_failed";
    public const string ManifestError = "manifest_error";
    public const string NoApplicableInstaller = "no_applicable_installer";
    public const string NoApplicableUpgrade = "no_applicable_upgrade";
    public const string AgreementsNotAccepted = "agreements_not_accepted";
    public const string InternalError = "internal_error";
    public const string UninstallFailed = "uninstall_failed";
    public const string VersionNotFound = "version_not_found";
    public const string NotRemovable = "not_removable";
}
