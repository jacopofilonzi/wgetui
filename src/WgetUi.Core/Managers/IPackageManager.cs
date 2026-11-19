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

    /// <summary>
    /// Every installed package, including ones no source offers (source <see cref="PackageSource.LocalId"/>).
    /// <see cref="Package.Version"/> is the installed version (null when unknown), <see cref="Package.AvailableVersion"/>
    /// the applicable update, if any. Read-only. Optional: managers that declare
    /// <see cref="ManagerCapabilities.ListInstalled"/> override it.
    /// </summary>
    Task<IReadOnlyList<Package>> GetInstalledAsync(CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<Package>>(NotSupported("listing installed packages"));

    /// <summary>
    /// Description, links and available versions of a package. Read-only. Optional: managers that declare
    /// <see cref="ManagerCapabilities.Details"/> override it.
    /// </summary>
    Task<PackageDetails> GetDetailsAsync(Package package, CancellationToken cancellationToken) =>
        Task.FromException<PackageDetails>(NotSupported("package details"));

    /// <summary>
    /// What the package's source (<see cref="Package.SourceId"/>) offers for its exact id, or null when the source does
    /// not offer it. Never looks at the installed state. Read-only. Optional: managers that declare
    /// <see cref="ManagerCapabilities.FindById"/> override it (used to resolve imported lists).
    /// </summary>
    Task<PackageOffer?> FindInSourceAsync(Package package, CancellationToken cancellationToken) =>
        Task.FromException<PackageOffer?>(NotSupported("finding a package by id"));

    /// <summary>
    /// Installs a package (the version in <see cref="OperationOptions.TargetVersion"/> when set). Acts for real on
    /// the machine.
    /// </summary>
    Task<OperationResult> InstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken);

    /// <summary>Upgrades an installed package. Acts for real on the machine.</summary>
    Task<OperationResult> UpgradeAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Uninstalls an installed package. Acts for real on the machine. Optional: managers that declare
    /// <see cref="ManagerCapabilities.Uninstall"/> override it.
    /// </summary>
    Task<OperationResult> UninstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Failure(ErrorCodes.NotSupported, $"{Id} does not support uninstalling."));

    private PackageManagerException NotSupported(string what) => new(ErrorCodes.NotSupported, $"{Id} does not support {what}.");
}
