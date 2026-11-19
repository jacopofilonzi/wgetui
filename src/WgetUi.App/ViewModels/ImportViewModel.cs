using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.ViewModels;

/// <summary>
/// The "Import packages" popup: every package of a list with what this machine has (installed or not, offered by its
/// source or not), a version mode (latest / pinned) and a checkbox per actionable row (see <see cref="ImportRowViewModel"/>).
/// </summary>
/// <remarks>
/// Resolution is read-only: one <see cref="ManagerRegistry.GetInstalledAsync"/> for the installed state (matched by
/// manager, source and id, so the winget <c>FindInstalled</c> caveats don't apply), then per package
/// <see cref="IPackageManager.FindInSourceAsync"/> (or <see cref="IPackageManager.GetDetailsAsync"/> for managers
/// without <see cref="ManagerCapabilities.FindById"/>), one package at a time per manager, managers in parallel.
/// Nothing is queued before "Install selected"; it then closes the popup and queues like a normal click, through
/// <see cref="PackageActions"/>: the installs first (one agreements modal unless automatic acceptance is on), then the
/// upgrades (never prompt). Declining the agreements queues nothing at all. Closing the popup queues nothing.
/// </remarks>
public sealed partial class ImportViewModel : ListPopupViewModel
{
    private readonly ManagerRegistry? _registry;
    private readonly PackageActions? _actions;
    private readonly ILogger _logger;
    private readonly PackageList? _list;
    private readonly string _label;
    private readonly string? _errorCode;

    /// <param name="list">The list to import.</param>
    /// <param name="label">Where it comes from: the file name, or the URL's host and file.</param>
    /// <param name="registry">Managers (lookups, capabilities, source names).</param>
    /// <param name="actions">Queues the chosen operations.</param>
    /// <param name="sourceName">Display name of a source (manager id, source id).</param>
    /// <param name="logger">Logger.</param>
    public ImportViewModel(PackageList list, string label, ManagerRegistry registry, PackageActions actions, Func<string, string, string> sourceName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(sourceName);
        _list = list;
        _label = label;
        _registry = registry;
        _actions = actions;
        _logger = logger;
        foreach (var entry in list.Packages)
        {
            Rows.Add(new ImportRowViewModel(entry, sourceName(entry.ManagerId, entry.SourceId), OnRowsChanged));
        }
    }

    private ImportViewModel(string label, string errorCode, ILogger logger)
    {
        _label = label;
        _errorCode = errorCode;
        _logger = logger;
    }

    /// <summary>The popup for a list that could not be read: the header and the error, nothing to install.</summary>
    public static ImportViewModel Failed(string label, string errorCode, ILogger logger) => new(label, errorCode, logger);

    public ObservableCollection<ImportRowViewModel> Rows { get; } = [];

    /// <summary>"home.wgetui · 12 packages · exported on 18/11/2026".</summary>
    public string Subtitle
    {
        get
        {
            if (_list is null)
            {
                return _label;
            }

            var parts = new List<string> { _label, Rows.Count == 1 ? Loc["Import_CountOne"] : Loc.Format("Import_Count", Rows.Count) };
            if (_list.ExportedAt is { } date)
            {
                parts.Add(Loc.Format("Import_ExportedOn", date.ToLocalTime().ToString("d", Loc.Culture)));
            }

            return string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial bool IsLoading { get; private set; }

    public string? ErrorText => _errorCode is null ? null : Loc.Format("Import_LoadFailed", Loc.Error(_errorCode));

    public bool HasError => _errorCode is not null;

    public bool HasList => _list is not null;

    public bool ShowEmpty => _list is not null && Rows.Count == 0;

    public bool IsPinnedMode { get; private set; }

    public bool IsLatestMode => !IsPinnedMode;

    public string ModeHint => Loc[IsPinnedMode ? "Import_ModePinnedHint" : "Import_ModeLatestHint"];

    public int SelectableCount => Rows.Count(r => r.CanSelect);

    public int SelectedCount => Rows.Count(r => r.IsSelected);

    public string SelectedText => Loc.Format("List_Selected", SelectedCount, SelectableCount);

    public string InstallText => Loc.Format("Import_Install", SelectedCount);

    /// <summary>Tri-state over the selectable rows: all, none, or some (null). Setting it selects or clears them all.</summary>
    public bool? SelectAll
    {
        get
        {
            var selectable = SelectableCount;
            var selected = SelectedCount;
            return selected == 0 ? false : selected == selectable ? true : null;
        }

        set
        {
            foreach (var row in Rows)
            {
                row.SetWanted(value == true);
            }

            OnRowsChanged();
        }
    }

    public bool CanSelectAll => SelectableCount > 0;

    /// <summary>What "Install selected" would queue now, in row order.</summary>
    public IReadOnlyList<ImportRequest> SelectedRequests() =>
        [.. Rows.Where(r => r.IsSelected).Select(r => r.Request).OfType<ImportRequest>()];

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_registry is null || Rows.Count == 0)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var installed = await _registry.GetInstalledAsync(cancellationToken);
            foreach (var error in installed.Errors)
            {
                LogInstalledFailed(_logger, error.ManagerId, error.ErrorCode);
            }

            var byKey = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in installed.Packages)
            {
                byKey.TryAdd(package.Key, package);
            }

            await Task.WhenAll(Rows.GroupBy(r => r.Entry.ManagerId, StringComparer.Ordinal)
                .Select(group => ResolveAsync(_registry.Find(group.Key), [.. group], byKey, cancellationToken)));
        }
        finally
        {
            IsLoading = false;
            OnRowsChanged();
        }
    }

    public override void OnLanguageChanged()
    {
        foreach (var row in Rows)
        {
            row.SetMode(IsPinnedMode);
        }

        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void SetLatestMode() => SetMode(pinned: false);

    [RelayCommand]
    private void SetPinnedMode() => SetMode(pinned: true);

    private bool CanInstall() => !IsLoading && SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install()
    {
        if (_actions is not { } actions || SelectedRequests() is not { Count: > 0 } requests)
        {
            return;
        }

        Choose(async () =>
        {
            var installs = requests.Where(r => r.Kind == OperationKind.Install).Select(r => new OperationRequest(r.Package, r.TargetVersion)).ToList();
            var upgrades = requests.Where(r => r.Kind == OperationKind.Upgrade).Select(r => new OperationRequest(r.Package)).ToList();
            if (installs.Count > 0 && (await actions.RequestAsync(installs, OperationKind.Install)).Declined)
            {
                return;
            }

            if (upgrades.Count > 0)
            {
                await actions.RequestAsync(upgrades, OperationKind.Upgrade);
            }

            LogQueued(_logger, installs.Count, upgrades.Count, IsPinnedMode);
        });
    }

    private void SetMode(bool pinned)
    {
        IsPinnedMode = pinned;
        foreach (var row in Rows)
        {
            row.SetMode(pinned);
        }

        // Always raised, also when the mode didn't change: the toggle buttons re-read their state.
        OnPropertyChanged(nameof(IsPinnedMode));
        OnPropertyChanged(nameof(IsLatestMode));
        OnPropertyChanged(nameof(ModeHint));
        OnRowsChanged();
    }

    private void OnRowsChanged()
    {
        OnPropertyChanged(nameof(SelectAll));
        OnPropertyChanged(nameof(CanSelectAll));
        OnPropertyChanged(nameof(SelectableCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(InstallText));
        InstallCommand.NotifyCanExecuteChanged();
    }

    // One manager: one package at a time (winget serializes its COM calls anyway); rows update as they resolve.
    private async Task ResolveAsync(IPackageManager? manager, List<ImportRowViewModel> rows, Dictionary<string, Package> installed, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = row.Entry;
            if (manager is null || manager.Availability is { IsAvailable: false } || !manager.Sources.Any(s => s.Id == entry.SourceId))
            {
                row.Resolve(ImportFinding.SourceUnavailable, null, null, manager?.Capabilities ?? ManagerCapabilities.None);
                continue;
            }

            var capabilities = manager.Capabilities;
            var installedPackage = installed.GetValueOrDefault(entry.Key);
            var package = new Package(entry.ManagerId, entry.SourceId, entry.Id, entry.Name, null);
            try
            {
                var (known, offer) = await LookUpAsync(manager, package, cancellationToken);
                var finding = installedPackage is not null ? ImportFinding.Installed : known && offer is null ? ImportFinding.NotFound : ImportFinding.NotInstalled;
                row.Resolve(finding, installedPackage, offer, capabilities);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The source could not be asked (offline, catalog error): nothing to offer for this row.
                LogLookupFailed(_logger, ex, entry.Key, ex is PackageManagerException pme ? pme.ErrorCode : ErrorCodes.Unexpected);
                row.Resolve(installedPackage is not null ? ImportFinding.Installed : ImportFinding.SourceUnavailable, installedPackage, null, capabilities);
            }
        }
    }

    // (known, offer): known = the manager could tell whether the source offers it; offer = what it offers, if anything.
    private static async Task<(bool Known, PackageOffer? Offer)> LookUpAsync(IPackageManager manager, Package package, CancellationToken cancellationToken)
    {
        if (manager.Capabilities.HasFlag(ManagerCapabilities.FindById))
        {
            return (true, await manager.FindInSourceAsync(package, cancellationToken));
        }

        if (manager.Capabilities.HasFlag(ManagerCapabilities.Details))
        {
            try
            {
                var details = await manager.GetDetailsAsync(package, cancellationToken);
                var offered = details.Package with { Version = details.Versions.Count > 0 ? details.Versions[0] : details.AvailableVersion, AvailableVersion = null };
                return (true, new PackageOffer(offered, details.Versions));
            }
            catch (PackageManagerException ex) when (ex.ErrorCode == ErrorCodes.PackageNotFound)
            {
                return (true, null);
            }
        }

        return (false, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import: could not list the installed packages of {ManagerId}: {ErrorCode}")]
    private static partial void LogInstalledFailed(ILogger logger, string managerId, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import: could not look up {PackageKey}: {ErrorCode}")]
    private static partial void LogLookupFailed(ILogger logger, Exception ex, string packageKey, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import: requested {Installs} install(s) and {Upgrades} upgrade(s), pinned versions: {Pinned}")]
    private static partial void LogQueued(ILogger logger, int installs, int upgrades, bool pinned);
}
