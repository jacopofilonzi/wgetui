using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Deployment;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;
using Package = WgetUi.Core.Models.Package;

namespace WgetUi.Managers.WinGet;

/// <summary>
/// winget through the Windows Package Manager COM API (Microsoft.Management.Deployment).
/// </summary>
/// <remarks>
/// Activation: the ComInterop package ships a registration-free activation factory
/// (Microsoft.Management.Deployment.dll, statically linked) plus a prebuilt C#/WinRT projection, so
/// <c>new PackageManager()</c> reaches the out-of-process winget server (App Installer) without
/// CoCreateInstance or any registration. winget refuses activation from an elevated process; that case
/// gets its own error code so the UI can tell the user to restart non-elevated.
/// Every COM call goes through <see cref="WinGetSerialExecutor"/> (one at a time): reads that overlap an
/// install return partial results. COM collections are read with index loops: C#/WinRT enumerators on
/// these out-of-proc vectors fail with E_NOINTERFACE.
/// Agreements: the shared catalog references never accept anything (msstore connects and searches without
/// it); an operation accepts package/source agreements only when its <see cref="OperationOptions"/> say so.
/// Exposes the winget and msstore catalogs only (not winget-font or user-added sources). The installed list
/// reports packages no exposed catalog offers under <see cref="PackageSource.LocalId"/>.
/// Uninstalls go through the installed catalog, silent and any scope. A specific version is installed through
/// <c>InstallOptions.PackageVersionId</c>, looked up in the package's <c>AvailableVersions</c>.
/// </remarks>
public sealed partial class WinGetManager : IPackageManager, IDisposable
{
    private const uint SearchResultLimit = 50;
    private static readonly Dictionary<string, PredefinedPackageCatalog> PredefinedCatalogs = new(StringComparer.Ordinal)
    {
        [WinGetMapping.WinGetSourceId] = PredefinedPackageCatalog.OpenWindowsCatalog,
        [WinGetMapping.MsStoreSourceId] = PredefinedPackageCatalog.MicrosoftStore,
    };
    private readonly ILogger<WinGetManager> _logger;
    private readonly WinGetSerialExecutor _executor = new(acquireTimeout: TimeSpan.FromMinutes(2), queryTimeout: TimeSpan.FromMinutes(2));
    private readonly Dictionary<string, PackageCatalogReference> _catalogRefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackageCatalog> _connectedCatalogs = new(StringComparer.Ordinal);
    private readonly INonRemovablePackages _nonRemovable;
    private PackageManager? _packageManager;

    public WinGetManager(ILogger<WinGetManager> logger)
        : this(logger, new AppxNonRemovablePackages(logger))
    {
    }

    internal WinGetManager(ILogger<WinGetManager> logger, INonRemovablePackages nonRemovable)
    {
        _logger = logger;
        _nonRemovable = nonRemovable;
    }

    public string Id => WinGetMapping.ManagerId;

    public string DisplayName => "winget";

    public ManagerCapabilities Capabilities =>
        ManagerCapabilities.Search | ManagerCapabilities.Install | ManagerCapabilities.ListUpgrades | ManagerCapabilities.Upgrade
        | ManagerCapabilities.Progress | ManagerCapabilities.Cancel | ManagerCapabilities.IncludeUnknownVersions
        | ManagerCapabilities.Agreements | ManagerCapabilities.InstallLocation | ManagerCapabilities.ListInstalled
        | ManagerCapabilities.Uninstall | ManagerCapabilities.Details | ManagerCapabilities.InstallVersion
        | ManagerCapabilities.FindById;

    public IReadOnlyList<PackageSource> Sources => WinGetMapping.Sources;

    public ManagerAvailability? Availability { get; private set; }

    public async Task<ManagerAvailability> InitializeAsync(CancellationToken cancellationToken)
    {
        if (Availability is { IsAvailable: true } available)
        {
            return available;
        }

        try
        {
            Availability = await _executor.RunQueryAsync("activation", Activate, cancellationToken).ConfigureAwait(false);
        }
        catch (PackageManagerException ex)
        {
            Availability = ManagerAvailability.Unavailable(ex.ErrorCode, ex.Message);
        }

        if (Availability.IsAvailable)
        {
            LogActivated(_packageManager?.Version);
        }
        else
        {
            LogActivationFailed(Availability.ErrorCode, Availability.Message);
        }

        return Availability;
    }

    public async Task<IReadOnlyList<Package>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return [];
        }

        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return await _executor.RunQueryAsync("search", () => SearchAllSources(trimmed), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Package>> GetUpgradesAsync(bool includeUnknown, CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return await _executor.RunQueryAsync("list upgrades", () => ListUpgrades(includeUnknown), cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageAgreements> GetAgreementsAsync(Package package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return await _executor.RunQueryAsync($"agreements of {package.Id}", () => ReadAgreements(package), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetInstallLocationAsync(Package package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return await _executor.RunQueryAsync($"install location of {package.Id}", () => ReadInstallLocation(package), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Package>> GetInstalledAsync(CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return await _executor.RunQueryAsync("list installed", ListInstalled, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageDetails> GetDetailsAsync(Package package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return await _executor.RunQueryAsync($"details of {package.Id}", () => ReadDetails(package), cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageOffer?> FindInSourceAsync(Package package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return await _executor.RunQueryAsync($"find {package.Id} in {package.SourceId}", () => FindOffer(package), cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult> InstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        RunOperationAsync(package, options, OperationKind.Install, progress, cancellationToken);

    public Task<OperationResult> UpgradeAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        RunOperationAsync(package, options, OperationKind.Upgrade, progress, cancellationToken);

    public Task<OperationResult> UninstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        RunOperationAsync(package, options, OperationKind.Uninstall, progress, cancellationToken);

    public void Dispose() => _executor.Dispose();

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}";

    private static FindPackagesOptions ExactIdOptions(string id)
    {
        var options = new FindPackagesOptions { ResultLimit = 1 };
        options.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = id });
        return options;
    }

    private static WinGetPackageData Snapshot(CatalogPackage package)
    {
        var available = package.DefaultInstallVersion;
        return new WinGetPackageData(
            package.Id,
            package.Name,
            available?.PackageCatalog?.Info?.Name,
            package.InstalledVersion?.Version,
            available?.Version,
            package.IsUpdateAvailable);
    }

    // Installed list: also the MSIX family names, to tell the packages Windows won't uninstall.
    private static WinGetPackageData InstalledSnapshot(CatalogPackage package)
    {
        var families = package.InstalledVersion?.PackageFamilyNames;
        var names = new List<string>(families?.Count ?? 0);
        for (var i = 0; families is not null && i < families.Count; i++)
        {
            names.Add(families[i]);
        }

        return Snapshot(package) with { PackageFamilyNames = names };
    }

    private ManagerAvailability Activate()
    {
        try
        {
            var packageManager = new PackageManager();
            var refs = new Dictionary<string, PackageCatalogReference>(StringComparer.Ordinal)
            {
                [WinGetMapping.WinGetSourceId] = packageManager.GetPredefinedPackageCatalog(PredefinedCatalogs[WinGetMapping.WinGetSourceId]),
                [WinGetMapping.MsStoreSourceId] = packageManager.GetPredefinedPackageCatalog(PredefinedCatalogs[WinGetMapping.MsStoreSourceId]),
            };
            _packageManager = packageManager;
            _catalogRefs.Clear();
            foreach (var (sourceId, catalogRef) in refs)
            {
                _catalogRefs[sourceId] = catalogRef;
            }

            return ManagerAvailability.Available;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException)
        {
            var elevated = IsElevated();
            return ManagerAvailability.Unavailable(
                elevated ? ErrorCodes.ComActivationElevated : ErrorCodes.ComActivationFailed,
                $"Creating the winget PackageManager COM object failed (elevated: {elevated}): {Describe(ex)}");
        }
    }

    private async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        var availability = Availability ?? await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.IsAvailable)
        {
            throw new PackageManagerException(availability.ErrorCode ?? ErrorCodes.ManagerUnavailable, availability.Message ?? "winget is unavailable.");
        }
    }

    private PackageManager RequirePackageManager() =>
        _packageManager ?? throw new PackageManagerException(ErrorCodes.ManagerUnavailable, "winget is not initialized.");

    // Runs under the executor lock.
    private PackageCatalog ConnectSource(string sourceId)
    {
        if (_connectedCatalogs.TryGetValue(sourceId, out var cached))
        {
            return cached;
        }

        if (!_catalogRefs.TryGetValue(sourceId, out var catalogRef))
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Unknown winget source '{sourceId}'.");
        }

        var connection = catalogRef.Connect();
        if (connection.Status != ConnectResultStatus.Ok)
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Connecting to winget source '{sourceId}' failed: {connection.Status}.");
        }

        _connectedCatalogs[sourceId] = connection.PackageCatalog;
        return connection.PackageCatalog;
    }

    // Runs under the executor lock. A fresh reference that accepts the source agreements, used only for
    // an operation the user accepted them for; the shared references never accept anything.
    private PackageCatalogReference CreateAcceptingReference(string sourceId)
    {
        if (!PredefinedCatalogs.TryGetValue(sourceId, out var predefined))
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Unknown winget source '{sourceId}'.");
        }

        var catalogRef = RequirePackageManager().GetPredefinedPackageCatalog(predefined);
        catalogRef.AcceptSourceAgreements = true;
        return catalogRef;
    }

    // Runs under the executor lock.
    private PackageCatalog ConnectSourceForOperation(string sourceId, bool acceptSourceAgreements)
    {
        if (!acceptSourceAgreements)
        {
            return ConnectSource(sourceId);
        }

        var connection = CreateAcceptingReference(sourceId).Connect();
        if (connection.Status != ConnectResultStatus.Ok)
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Connecting to winget source '{sourceId}' failed: {connection.Status}.");
        }

        return connection.PackageCatalog;
    }

    // Runs under the executor lock. Installed packages correlated with the remote catalogs.
    private PackageCatalog ConnectInstalled(bool acceptSourceAgreements = false)
    {
        var options = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs };
        foreach (var (sourceId, catalogRef) in _catalogRefs)
        {
            options.Catalogs.Add(acceptSourceAgreements ? CreateAcceptingReference(sourceId) : catalogRef);
        }

        var connection = RequirePackageManager().CreateCompositePackageCatalog(options).Connect();
        if (connection.Status != ConnectResultStatus.Ok)
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Connecting to the installed packages catalog failed: {connection.Status}.");
        }

        return connection.PackageCatalog;
    }

    private IReadOnlyList<Package> SearchAllSources(string query)
    {
        var results = new List<Package>();
        var failures = new List<PackageManagerException>();
        foreach (var source in WinGetMapping.Sources)
        {
            try
            {
                results.AddRange(SearchSource(source.Id, query));
            }
            catch (PackageManagerException ex)
            {
                LogSourceFailed(ex, source.Id, ex.ErrorCode);
                failures.Add(ex);
            }
            catch (COMException ex)
            {
                // A stale connection fails with a COM error: reconnect next time.
                _connectedCatalogs.Remove(source.Id);
                var wrapped = new PackageManagerException(ErrorCodes.CatalogError, $"Searching winget source '{source.Id}' failed: {Describe(ex)}", ex);
                LogSourceFailed(wrapped, source.Id, wrapped.ErrorCode);
                failures.Add(wrapped);
            }
        }

        if (failures.Count == WinGetMapping.Sources.Count)
        {
            throw failures[0];
        }

        return WinGetMapping.Distinct(results);
    }

    private List<Package> SearchSource(string sourceId, string query)
    {
        var catalog = ConnectSource(sourceId);
        var options = new FindPackagesOptions { ResultLimit = SearchResultLimit };
        options.Selectors.Add(new PackageMatchFilter { Field = PackageMatchField.CatalogDefault, Option = PackageFieldMatchOption.ContainsCaseInsensitive, Value = query });
        var result = catalog.FindPackages(options);
        if (result.Status != FindPackagesResultStatus.Ok)
        {
            throw new PackageManagerException(WinGetMapping.ToErrorCode(result.Status), $"Searching winget source '{sourceId}' failed: {result.Status}.");
        }

        var packages = new List<Package>();
        var matches = result.Matches;
        for (var i = 0; i < matches.Count; i++)
        {
            var catalogPackage = matches[i].CatalogPackage;
            try
            {
                if (WinGetMapping.ToSearchResult(Snapshot(catalogPackage), sourceId) is { } package)
                {
                    packages.Add(package);
                }
            }
            catch (COMException ex)
            {
                // One broken manifest must not hide the other results.
                LogPackageSkipped(ex, sourceId, i);
            }
        }

        return packages;
    }

    // Runs under the executor lock. Read-only: nothing is accepted here.
    private PackageAgreements ReadAgreements(Package package)
    {
        try
        {
            var found = ConnectSource(package.SourceId).FindPackages(ExactIdOptions(package.Id));
            if (found.Status != FindPackagesResultStatus.Ok)
            {
                throw new PackageManagerException(WinGetMapping.ToErrorCode(found.Status), $"Reading agreements of {package.Id} failed: {found.Status}.");
            }

            if (found.Matches.Count == 0)
            {
                throw new PackageManagerException(ErrorCodes.PackageNotFound, $"Reading agreements of {package.Id}: not found in '{package.SourceId}'.");
            }

            var metadata = found.Matches[0].CatalogPackage.DefaultInstallVersion?.GetCatalogPackageMetadata();
            var packageTerms = new List<Agreement>();
            var comTerms = metadata?.Agreements;
            for (var i = 0; comTerms is not null && i < comTerms.Count; i++)
            {
                if (WinGetMapping.ToAgreement(comTerms[i].Label, comTerms[i].Text, comTerms[i].Url) is { } agreement)
                {
                    packageTerms.Add(agreement);
                }
            }

            var sourceTerms = new List<Agreement>();
            var comSourceTerms = _catalogRefs[package.SourceId].SourceAgreements;
            for (var i = 0; i < comSourceTerms.Count; i++)
            {
                if (WinGetMapping.ToAgreement(comSourceTerms[i].Label, comSourceTerms[i].Text, comSourceTerms[i].Url) is { } agreement)
                {
                    sourceTerms.Add(agreement);
                }
            }

            var (license, licenseUrl) = WinGetMapping.ToLicense(metadata?.License, metadata?.LicenseUrl);
            return new PackageAgreements(package, packageTerms, sourceTerms, license, licenseUrl);
        }
        catch (COMException ex)
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Reading agreements of {package.Id} failed: {Describe(ex)}", ex);
        }
    }

    // Runs under the executor lock. Read-only. Empty metadata (MSI without a location, runtimes…) is null.
    private string? ReadInstallLocation(Package package)
    {
        try
        {
            var found = ConnectInstalled().FindPackages(ExactIdOptions(package.Id));
            if (found.Status != FindPackagesResultStatus.Ok || found.Matches.Count == 0)
            {
                return null;
            }

            var location = found.Matches[0].CatalogPackage.InstalledVersion?.GetMetadata(PackageVersionMetadataField.InstalledLocation);
            return string.IsNullOrWhiteSpace(location) ? null : location;
        }
        catch (COMException ex)
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Reading the install location of {package.Id} failed: {Describe(ex)}", ex);
        }
    }

    // Runs under the executor lock. Read-only. The Id filter does not match local entries ("ARP\Machine\X64\…"),
    // so those fall back to scanning the whole installed list (about a second).
    private CatalogPackage? FindInstalled(string id)
    {
        var catalog = ConnectInstalled();
        var exact = catalog.FindPackages(ExactIdOptions(id));
        if (exact.Status == FindPackagesResultStatus.Ok && exact.Matches.Count > 0)
        {
            return exact.Matches[0].CatalogPackage;
        }

        var all = catalog.FindPackages(new FindPackagesOptions());
        if (all.Status != FindPackagesResultStatus.Ok)
        {
            throw new PackageManagerException(WinGetMapping.ToErrorCode(all.Status), $"Listing installed packages failed: {all.Status}.");
        }

        var matches = all.Matches;
        for (var i = 0; i < matches.Count; i++)
        {
            var candidate = matches[i].CatalogPackage;
            if (string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    // Runs under the executor lock. Read-only. Packages Windows won't uninstall come back with IsRemovable = false.
    private IReadOnlyList<Package> ListInstalled()
    {
        var nonRemovable = _nonRemovable.GetNonRemovableFamilyNames();
        try
        {
            var result = ConnectInstalled().FindPackages(new FindPackagesOptions());
            if (result.Status != FindPackagesResultStatus.Ok)
            {
                throw new PackageManagerException(WinGetMapping.ToErrorCode(result.Status), $"Listing installed packages failed: {result.Status}.");
            }

            var packages = new List<Package>();
            var matches = result.Matches;
            for (var i = 0; i < matches.Count; i++)
            {
                try
                {
                    if (WinGetMapping.ToInstalled(InstalledSnapshot(matches[i].CatalogPackage), nonRemovable) is { } package)
                    {
                        packages.Add(package);
                    }
                }
                catch (COMException ex)
                {
                    LogPackageSkipped(ex, "installed", i);
                }
            }

            return WinGetMapping.Distinct(packages);
        }
        catch (COMException ex)
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Listing installed packages failed: {Describe(ex)}", ex);
        }
    }

    // Runs under the executor lock. Read-only. Installed state from the installed catalog, metadata and versions
    // from the package's own source (local packages only have what the installed entry knows).
    private PackageDetails ReadDetails(Package package)
    {
        try
        {
            var installed = FindInstalled(package.Id);
            CatalogPackage? remote = null;
            if (package.SourceId != PackageSource.LocalId)
            {
                var found = ConnectSource(package.SourceId).FindPackages(ExactIdOptions(package.Id));
                if (found.Status != FindPackagesResultStatus.Ok)
                {
                    throw new PackageManagerException(WinGetMapping.ToErrorCode(found.Status), $"Reading details of {package.Id} failed: {found.Status}.");
                }

                remote = found.Matches.Count > 0 ? found.Matches[0].CatalogPackage : null;
            }

            if (installed is null && remote is null)
            {
                throw new PackageManagerException(ErrorCodes.PackageNotFound, $"Reading details of {package.Id}: not found in '{package.SourceId}' nor installed.");
            }

            var metadata = remote?.DefaultInstallVersion?.GetCatalogPackageMetadata();
            var versions = new List<string>();
            var available = remote?.AvailableVersions;
            for (var i = 0; available is not null && i < available.Count; i++)
            {
                versions.Add(available[i].Version);
            }

            var installedVersion = installed?.InstalledVersion;
            var publisher = metadata?.Publisher;
            if (string.IsNullOrWhiteSpace(publisher) && installedVersion is not null)
            {
                publisher = installedVersion.GetMetadata(PackageVersionMetadataField.PublisherDisplayName);
            }

            var data = new WinGetDetailsData(
                metadata?.Description,
                metadata?.ShortDescription,
                publisher,
                metadata?.PackageUrl,
                metadata?.PublisherUrl,
                metadata?.License,
                metadata?.LicenseUrl,
                metadata?.ReleaseNotesUrl,
                installedVersion?.Version,
                installed is { IsUpdateAvailable: true } ? installed.DefaultInstallVersion?.Version : null,
                versions);
            return WinGetMapping.ToDetails(package, data);
        }
        catch (COMException ex)
        {
            // A stale connection fails with a COM error: reconnect next time.
            _connectedCatalogs.Remove(package.SourceId);
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Reading details of {package.Id} failed: {Describe(ex)}", ex);
        }
    }

    // Runs under the executor lock. Read-only: one exact-id find in the package's own source, never the installed
    // catalog (no scan of the installed list, unlike ReadDetails). Sources we don't expose offer nothing.
    private PackageOffer? FindOffer(Package package)
    {
        if (WinGetMapping.ToSourceId(package.SourceId) is null)
        {
            return null;
        }

        try
        {
            var found = ConnectSource(package.SourceId).FindPackages(ExactIdOptions(package.Id));
            if (found.Status != FindPackagesResultStatus.Ok)
            {
                throw new PackageManagerException(WinGetMapping.ToErrorCode(found.Status), $"Finding {package.Id} in {package.SourceId} failed: {found.Status}.");
            }

            if (found.Matches.Count == 0)
            {
                return null;
            }

            var remote = found.Matches[0].CatalogPackage;
            var versions = new List<string?>();
            var available = remote.AvailableVersions;
            for (var i = 0; available is not null && i < available.Count; i++)
            {
                versions.Add(available[i].Version);
            }

            return WinGetMapping.ToOffer(package, remote.Id, remote.Name, remote.DefaultInstallVersion?.Version, versions);
        }
        catch (COMException ex)
        {
            // A stale connection fails with a COM error: reconnect next time.
            _connectedCatalogs.Remove(package.SourceId);
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Finding {package.Id} in {package.SourceId} failed: {Describe(ex)}", ex);
        }
    }

    private IReadOnlyList<Package> ListUpgrades(bool includeUnknown)
    {
        try
        {
            var result = ConnectInstalled().FindPackages(new FindPackagesOptions());
            if (result.Status != FindPackagesResultStatus.Ok)
            {
                throw new PackageManagerException(WinGetMapping.ToErrorCode(result.Status), $"Listing installed packages failed: {result.Status}.");
            }

            var packages = new List<Package>();
            var matches = result.Matches;
            for (var i = 0; i < matches.Count; i++)
            {
                try
                {
                    if (WinGetMapping.ToUpgrade(Snapshot(matches[i].CatalogPackage), includeUnknown) is { } package)
                    {
                        packages.Add(package);
                    }
                }
                catch (COMException ex)
                {
                    LogPackageSkipped(ex, "installed", i);
                }
            }

            return WinGetMapping.Distinct(packages);
        }
        catch (COMException ex)
        {
            throw new PackageManagerException(ErrorCodes.CatalogError, $"Listing winget upgrades failed: {Describe(ex)}", ex);
        }
    }

    private async Task<OperationResult> RunOperationAsync(Package package, OperationOptions options, OperationKind kind, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        var what = kind switch
        {
            OperationKind.Install when options.TargetVersion is not null => $"install {package.Id} {options.TargetVersion}",
            OperationKind.Install => $"install {package.Id}",
            OperationKind.Upgrade => $"upgrade {package.Id}",
            _ => $"uninstall {package.Id}",
        };
        if (!string.Equals(package.ManagerId, Id, StringComparison.Ordinal))
        {
            return OperationResult.Failure(ErrorCodes.ManagerNotFound, $"{what}: package belongs to '{package.ManagerId}', not winget.");
        }

        try
        {
            await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            return await _executor.RunOperationAsync(
                what,
                () => kind == OperationKind.Uninstall
                    ? RunLockedUninstallAsync(package, what, progress, cancellationToken)
                    : RunLockedOperationAsync(package, options, kind == OperationKind.Upgrade, what, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancelled();
        }
        catch (PackageManagerException ex)
        {
            return OperationResult.Failure(ex.ErrorCode, ex.Message);
        }
        catch (COMException ex)
        {
            return OperationResult.Failure(ErrorCodes.InternalError, $"winget {what} failed: {Describe(ex)}");
        }
    }

    // Runs under the executor lock for the whole install.
    private async Task<OperationResult> RunLockedOperationAsync(Package package, OperationOptions operationOptions, bool isUpgrade, string what, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = isUpgrade
            ? ConnectInstalled(operationOptions.AcceptSourceAgreements)
            : ConnectSourceForOperation(package.SourceId, operationOptions.AcceptSourceAgreements);
        var found = catalog.FindPackages(ExactIdOptions(package.Id));
        if (found.Status != FindPackagesResultStatus.Ok)
        {
            return OperationResult.Failure(WinGetMapping.ToErrorCode(found.Status), $"winget {what}: lookup failed: {found.Status}.");
        }

        if (found.Matches.Count == 0)
        {
            return OperationResult.Failure(ErrorCodes.PackageNotFound, $"winget {what}: package not found in '{(isUpgrade ? "installed" : package.SourceId)}'.");
        }

        var catalogPackage = found.Matches[0].CatalogPackage;
        if (isUpgrade && !catalogPackage.IsUpdateAvailable)
        {
            return OperationResult.Failure(ErrorCodes.NoApplicableUpgrade, $"winget {what}: no upgrade is available any more.");
        }

        var options = new InstallOptions
        {
            PackageInstallMode = PackageInstallMode.Silent,
            PackageInstallScope = PackageInstallScope.Any,
            // Only when the user accepted them (modal or automatic-acceptance setting), decided by Core.
            AcceptPackageAgreements = operationOptions.AcceptPackageAgreements,
            AllowUpgradeToUnknownVersion = isUpgrade && package.IsVersionUnknown,
        };

        if (!isUpgrade && operationOptions.TargetVersion is { } target)
        {
            var available = catalogPackage.AvailableVersions;
            var versions = new List<string?>(available.Count);
            for (var i = 0; i < available.Count; i++)
            {
                versions.Add(available[i].Version);
            }

            var index = WinGetMapping.IndexOfVersion(versions, target);
            if (index < 0)
            {
                return OperationResult.Failure(ErrorCodes.VersionNotFound, $"winget {what}: version {target} is not offered by '{package.SourceId}'.");
            }

            options.PackageVersionId = available[index];
        }

        LogOperationStarting(what);
        var packageManager = RequirePackageManager();
        var operation = isUpgrade
            ? packageManager.UpgradePackageAsync(catalogPackage, options)
            : packageManager.InstallPackageAsync(catalogPackage, options);
        var comProgress = new ComProgress(progress);
        var result = await operation.AsTask(cancellationToken, comProgress).ConfigureAwait(false);
        return WinGetMapping.ToResult(result.Status, result.ExtendedErrorCode?.HResult ?? 0, result.InstallerErrorCode, result.RebootRequired, what);
    }

    // Runs under the executor lock for the whole uninstall.
    private async Task<OperationResult> RunLockedUninstallAsync(Package package, string what, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = FindInstalled(package.Id);
        if (installed?.InstalledVersion is null)
        {
            return OperationResult.Failure(ErrorCodes.PackageNotFound, $"winget {what}: package is not installed any more.");
        }

        // Checked again against the OS, not only through Package.IsRemovable (the queue's check).
        if (!WinGetMapping.IsRemovable(InstalledSnapshot(installed), _nonRemovable.GetNonRemovableFamilyNames()))
        {
            return OperationResult.Failure(ErrorCodes.NotRemovable, $"winget {what}: Windows does not let users uninstall this system package.");
        }

        var options = new UninstallOptions
        {
            PackageUninstallMode = PackageUninstallMode.Silent,
            PackageUninstallScope = PackageUninstallScope.Any,
        };

        LogOperationStarting(what);
        var operation = RequirePackageManager().UninstallPackageAsync(installed, options);
        var result = await operation.AsTask(cancellationToken, new ComUninstallProgress(progress)).ConfigureAwait(false);
        return WinGetMapping.ToUninstallResult(result.Status, result.ExtendedErrorCode?.HResult ?? 0, result.UninstallerErrorCode, result.RebootRequired, what);
    }

    private sealed class ComUninstallProgress(IProgress<OperationProgress> target) : IProgress<UninstallProgress>
    {
        public void Report(UninstallProgress value) => target.Report(WinGetMapping.ToUninstallProgress(value.State, value.UninstallationProgress));
    }

    private sealed class ComProgress(IProgress<OperationProgress> target) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) =>
            target.Report(WinGetMapping.ToProgress(value.State, value.DownloadProgress, value.InstallationProgress));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "winget COM activated (server version {Version})")]
    private partial void LogActivated(string? version);

    [LoggerMessage(Level = LogLevel.Error, Message = "winget COM activation failed: {ErrorCode} {Detail}")]
    private partial void LogActivationFailed(string? errorCode, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "winget source {SourceId} failed: {ErrorCode}")]
    private partial void LogSourceFailed(Exception ex, string sourceId, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped unreadable winget package #{Index} from {SourceId}")]
    private partial void LogPackageSkipped(Exception ex, string sourceId, int index);

    [LoggerMessage(Level = LogLevel.Information, Message = "winget {What}: starting")]
    private partial void LogOperationStarting(string what);
}
