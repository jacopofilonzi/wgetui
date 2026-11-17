using WgetUi.Core.Models;

namespace WgetUi.Core.Managers;

/// <summary>
/// A package manager (winget, chocolatey, npm…). Implementations live in their own project and are
/// registered once at startup; the rest of the app only talks to this interface.
/// </summary>
/// <remarks>
/// Queries throw <see cref="PackageManagerException"/> on failure and <see cref="OperationCanceledException"/>
/// when cancelled. Install and upgrade report failures through <see cref="OperationResult"/> instead, so a
/// failed package never needs exception handling in the queue. Implementations must be thread-safe.
/// </remarks>
public interface IPackageManager
{
    /// <summary>Stable id, used in <see cref="Package.ManagerId"/> and settings.</summary>
    string Id { get; }

    string DisplayName { get; }

    ManagerCapabilities Capabilities { get; }

    /// <summary>Sources this manager exposes. Known up front, even before initialization.</summary>
    IReadOnlyList<PackageSource> Sources { get; }

    /// <summary>Availability from the last <see cref="InitializeAsync"/>; null before the first call.</summary>
    ManagerAvailability? Availability { get; }

    /// <summary>Connects to the manager. Safe to call more than once; never throws for an unavailable manager.</summary>
    Task<ManagerAvailability> InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Package>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Installed packages with a newer version available.</summary>
    /// <param name="includeUnknown">Also return packages whose installed version is unknown.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<Package>> GetUpgradesAsync(bool includeUnknown, CancellationToken cancellationToken);

    /// <summary>Agreements the user must accept before installing or upgrading the package. Read-only.</summary>
    Task<PackageAgreements> GetAgreementsAsync(Package package, CancellationToken cancellationToken);

    /// <summary>
    /// Folder an installed package lives in, or null when unknown. Read-only. Optional: managers that declare
    /// <see cref="ManagerCapabilities.InstallLocation"/> override it; the default knows nothing.
    /// </summary>
    Task<string?> GetInstallLocationAsync(Package package, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <summary>Installs a package. Acts for real on the machine.</summary>
    Task<OperationResult> InstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken);

    /// <summary>Upgrades an installed package. Acts for real on the machine.</summary>
    Task<OperationResult> UpgradeAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken);
}
