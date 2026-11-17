using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Deployment;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
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
/// Exposes the winget and msstore catalogs only (not winget-font or user-added sources).
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
    private PackageManager? _packageManager;

    public WinGetManager(ILogger<WinGetManager> logger) => _logger = logger;

    public string Id => WinGetMapping.ManagerId;

    public string DisplayName => "winget";

    public ManagerCapabilities Capabilities =>
        ManagerCapabilities.Search | ManagerCapabilities.Install | ManagerCapabilities.ListUpgrades | ManagerCapabilities.Upgrade
        | ManagerCapabilities.Progress | ManagerCapabilities.Cancel | ManagerCapabilities.IncludeUnknownVersions
        | ManagerCapabilities.Agreements | ManagerCapabilities.InstallLocation;

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

    public Task<OperationResult> InstallAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        RunOperationAsync(package, options, isUpgrade: false, progress, cancellationToken);

    public Task<OperationResult> UpgradeAsync(Package package, OperationOptions options, IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        RunOperationAsync(package, options, isUpgrade: true, progress, cancellationToken);

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

    private async Task<OperationResult> RunOperationAsync(Package package, OperationOptions options, bool isUpgrade, IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        var what = $"{(isUpgrade ? "upgrade" : "install")} {package.Id}";
        if (!string.Equals(package.ManagerId, Id, StringComparison.Ordinal))
        {
            return OperationResult.Failure(ErrorCodes.ManagerNotFound, $"{what}: package belongs to '{package.ManagerId}', not winget.");
        }

        try
        {
            await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            return await _executor.RunOperationAsync(what, () => RunLockedOperationAsync(package, options, isUpgrade, what, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
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

        LogOperationStarting(what);
        var packageManager = RequirePackageManager();
        var operation = isUpgrade
            ? packageManager.UpgradePackageAsync(catalogPackage, options)
            : packageManager.InstallPackageAsync(catalogPackage, options);
        var comProgress = new ComProgress(progress);
        var result = await operation.AsTask(cancellationToken, comProgress).ConfigureAwait(false);
        return WinGetMapping.ToResult(result.Status, result.ExtendedErrorCode?.HResult ?? 0, result.InstallerErrorCode, result.RebootRequired, what);
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
