using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.App.ViewModels;

/// <summary>Which page opened the details popup: decides the primary action.</summary>
public enum DetailsContext
{
    Search,
    Updates,
    Installed,
}

/// <summary>What the popup's buttons do, supplied by the page that opened it. A null entry hides the button.</summary>
/// <param name="Primary">Install (Search), Upgrade (Updates) or Uninstall (Installed).</param>
/// <param name="Ignore">Ignores the available version (receives the package with that version).</param>
/// <param name="InstallVersion">Installs one specific version.</param>
public sealed record PackageDetailsActions(
    Func<Package, Task>? Primary = null,
    Action<Package>? Ignore = null,
    Func<Package, string, Task>? InstallVersion = null);

/// <summary>
/// The package details popup: loads <see cref="PackageDetails"/> from the manager (read-only) and offers the
/// actions that fit the page and the manager's capabilities.
/// </summary>
/// <remarks>
/// An action never runs while the popup is open: it is stored in <see cref="ChosenAction"/>, the popup closes
/// (<see cref="CloseRequested"/>) and the presenter runs it afterwards, because an install may need the
/// agreements modal and only one dialog can be open at a time.
/// </remarks>
public sealed partial class PackageDetailsViewModel : ViewModelBase
{
    private readonly ManagerRegistry _registry;
    private readonly PackageDetailsActions _actions;
    private readonly ILogger _logger;
    private PackageDetails _details;
    private string? _errorCode;

    public PackageDetailsViewModel(Package package, string sourceName, DetailsContext context, ManagerRegistry registry, PackageDetailsActions actions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(registry);
        Package = package;
        SourceName = sourceName;
        Context = context;
        _registry = registry;
        _actions = actions;
        _logger = logger;
        Capabilities = registry.Find(package.ManagerId)?.Capabilities ?? ManagerCapabilities.None;
        _details = PackageDetails.Minimal(package) with
        {
            // Search results carry the latest available version in Version, not an installed one.
            InstalledVersion = context == DetailsContext.Search ? null : package.Version,
        };
        Refresh();
    }

    /// <summary>Raised when the popup should close (Close, X, or an action was chosen).</summary>
    public event EventHandler? CloseRequested;

    public Package Package { get; }

    public DetailsContext Context { get; }

    public ManagerCapabilities Capabilities { get; }

    public string Name => Package.Name;

    public string Id => Package.Id;

    public string SourceId => Package.SourceId;

    public string SourceName { get; }

    /// <summary>The action to run once the popup has closed, or null.</summary>
    public Func<Task>? ChosenAction { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial bool IsLoading { get; set; }

    public string? ErrorText => _errorCode is null ? null : Loc.Format("Details_LoadFailed", Loc.Error(_errorCode));

    public bool HasError => _errorCode is not null && !IsLoading;

    public string? Publisher => _details.Publisher;

    public bool HasPublisher => Publisher is not null;

    public string? Description => _details.Description;

    public bool HasDescription => Description is not null;

    public string? InstalledVersion => _details.InstalledVersion;

    public bool HasInstalledVersion => InstalledVersion is not null;

    public string? AvailableVersion => _details.AvailableVersion;

    public bool HasAvailableVersion => AvailableVersion is not null;

    public string? License => _details.License;

    public Uri? LicenseUri => ToUri(_details.LicenseUrl);

    public bool HasLicense => License is not null;

    public bool HasLicenseLink => HasLicense && LicenseUri is not null;

    public bool HasLicenseText => HasLicense && LicenseUri is null;

    public Uri? HomepageUri => ToUri(_details.HomepageUrl);

    /// <summary>The homepage host ("7-zip.org"), shorter than the whole link.</summary>
    public string? HomepageText => HomepageUri is { } uri ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host) : null;

    public bool HasHomepage => HomepageUri is not null;

    public Uri? ReleaseNotesUri => ToUri(_details.ReleaseNotesUrl);

    public bool HasReleaseNotes => ReleaseNotesUri is not null;

    public ObservableCollection<PackageVersionItemViewModel> Versions { get; } = [];

    public bool HasVersions => Versions.Count > 0;

    public string? PrimaryText { get; private set; }

    public bool ShowPrimary => PrimaryText is not null;

    public string? IgnoreText => AvailableVersion is null ? null : Loc.Format("Details_Ignore", AvailableVersion);

    public bool ShowIgnore => _actions.Ignore is not null && AvailableVersion is not null;

    /// <summary>Reads the details from the manager. Failures leave the basic info and show an error line.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var manager = _registry.Find(Package.ManagerId);
        if (manager is null || !manager.Capabilities.HasFlag(ManagerCapabilities.Details))
        {
            return;
        }

        IsLoading = true;
        try
        {
            _details = await manager.GetDetailsAsync(Package, cancellationToken);
            _errorCode = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (PackageManagerException ex)
        {
            LogLoadFailed(_logger, ex, Package.Key, ex.ErrorCode);
            _errorCode = ex.ErrorCode;
        }
        catch (Exception ex)
        {
            // A broken manager must not take the popup down: show what we have.
            LogLoadFailed(_logger, ex, Package.Key, ErrorCodes.Unexpected);
            _errorCode = ErrorCodes.Unexpected;
        }
        finally
        {
            IsLoading = false;
        }

        Refresh();
    }

    /// <summary>Runs the chosen action, if any (called by the presenter after the popup closed).</summary>
    public Task RunChosenActionAsync() => ChosenAction?.Invoke() ?? Task.CompletedTask;

    public override void OnLanguageChanged() => Refresh();

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Primary()
    {
        if (_actions.Primary is { } primary && ShowPrimary)
        {
            Choose(() => primary(PackageForAction()));
        }
    }

    [RelayCommand]
    private void Ignore()
    {
        if (_actions.Ignore is { } ignore && ShowIgnore)
        {
            var package = PackageForAction();
            Choose(() =>
            {
                ignore(package);
                return Task.CompletedTask;
            });
        }
    }

    private void InstallVersion(string version)
    {
        if (_actions.InstallVersion is { } install)
        {
            Choose(() => install(Package, version));
        }
    }

    private void Choose(Func<Task> action)
    {
        ChosenAction = action;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // The package as the manager now describes it (the available version may be newer than the row's).
    private Package PackageForAction() =>
        AvailableVersion is { } available && Context != DetailsContext.Search ? Package with { AvailableVersion = available } : Package;

    private string? ComputePrimaryText() => Context switch
    {
        DetailsContext.Updates when AvailableVersion is not null && Capabilities.HasFlag(ManagerCapabilities.Upgrade) => Loc.Format("Details_UpgradeTo", AvailableVersion),
        DetailsContext.Search when InstalledVersion is null && Capabilities.HasFlag(ManagerCapabilities.Install) => Loc["Search_Install"],
        DetailsContext.Installed when Capabilities.HasFlag(ManagerCapabilities.Uninstall) => Loc["Installed_Uninstall"],
        _ => null,
    };

    private void Refresh()
    {
        PrimaryText = _actions.Primary is null ? null : ComputePrimaryText();
        var canInstallVersion = _actions.InstallVersion is not null
            && Capabilities.HasFlag(ManagerCapabilities.InstallVersion)
            && Package.SourceId != PackageSource.LocalId;
        Versions.Clear();
        for (var i = 0; i < _details.Versions.Count; i++)
        {
            var version = _details.Versions[i];
            var isInstalled = string.Equals(version, InstalledVersion, StringComparison.OrdinalIgnoreCase);
            Versions.Add(new PackageVersionItemViewModel(version, isLatest: i == 0, isInstalled, canInstallVersion && !isInstalled, InstallVersion));
        }

        OnPropertyChanged(string.Empty);
    }

    private static Uri? ToUri(string? url) => url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the details of {PackageKey}: {ErrorCode}")]
    private static partial void LogLoadFailed(ILogger logger, Exception ex, string packageKey, string errorCode);
}

/// <summary>One row of "Available versions".</summary>
public sealed partial class PackageVersionItemViewModel
{
    private readonly Action<string> _install;

    public PackageVersionItemViewModel(string version, bool isLatest, bool isInstalled, bool canInstall, Action<string> install)
    {
        Version = version;
        IsLatest = isLatest;
        IsInstalled = isInstalled;
        CanInstall = canInstall;
        _install = install;
    }

    public string Version { get; }

    public bool IsLatest { get; }

    public bool IsInstalled { get; }

    public bool CanInstall { get; }

    [RelayCommand]
    private void Install() => _install(Version);
}
