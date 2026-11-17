namespace WgetUi.Core.Models;

/// <summary>
/// What a manager knows about a package, for the details popup. Every field but <see cref="Package"/> and
/// <see cref="Versions"/> may be missing; the UI hides what is null. Read-only data.
/// </summary>
/// <param name="Package">The package the details are about.</param>
/// <param name="Description">Description from the manifest (may be several lines).</param>
/// <param name="Publisher">Publisher name.</param>
/// <param name="HomepageUrl">Package homepage (http/https only).</param>
/// <param name="License">License name.</param>
/// <param name="LicenseUrl">License link (http/https only).</param>
/// <param name="ReleaseNotesUrl">Release notes link (http/https only).</param>
/// <param name="InstalledVersion">Installed version, null when not installed or unknown.</param>
/// <param name="AvailableVersion">Version the manager would install or upgrade to, null when none.</param>
/// <param name="Versions">Versions the source offers, newest first (empty when unknown).</param>
public sealed record PackageDetails(
    Package Package,
    string? Description,
    string? Publisher,
    string? HomepageUrl,
    string? License,
    string? LicenseUrl,
    string? ReleaseNotesUrl,
    string? InstalledVersion,
    string? AvailableVersion,
    IReadOnlyList<string> Versions)
{
    /// <summary>Only the package: what the UI shows when the manager has no details.</summary>
    public static PackageDetails Minimal(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new(package, null, null, null, null, null, null, package.AvailableVersion is null ? null : package.Version, package.AvailableVersion, []);
    }

    /// <summary>Keeps http/https links only (manifests sometimes carry store deep links or junk).</summary>
    public static string? WebLink(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.OriginalString
            : null;
}
