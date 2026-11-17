using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WgetUi.App.Services;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>Asks "Uninstall X?" before anything is queued (implemented by the App's modal; tests use a fake).</summary>
public interface IUninstallConfirmer
{
    Task<bool> ConfirmUninstallAsync(Package package);
}

/// <summary>
/// Every installed package of the managers that can list them (packages no source offers included, under
/// <see cref="PackageSource.LocalId"/>), with a name filter, details and Uninstall (after a confirmation).
/// </summary>
/// <remarks>Loaded the first time the page is shown (<see cref="EnsureLoaded"/>) and on Refresh.</remarks>
public sealed partial class InstalledViewModel : ViewModelBase
{
    private readonly ManagerRegistry _registry;
    private readonly PackageActions _actions;
    private readonly PackageDetailsLauncher _details;
    private readonly IgnoredUpdatesService _ignored;
    private readonly IUninstallConfirmer _confirmer;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<InstalledViewModel> _logger;
    private readonly List<PackageRowViewModel> _rows = [];
    private IReadOnlyList<Package> _packages = [];
    private IReadOnlyList<ManagerError> _lastErrors = [];

    public InstalledViewModel(
        ManagerRegistry registry,
        PackageActions actions,
        PackageDetailsLauncher details,
        IgnoredUpdatesService ignored,
        IUninstallConfirmer confirmer,
        OperationQueue queue,
        IUiDispatcher dispatcher,
        ILogger<InstalledViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _registry = registry;
        _actions = actions;
        _details = details;
        _ignored = ignored;
        _confirmer = confirmer;
        _dispatcher = dispatcher;
        _logger = logger;
        Packages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNone));
        queue.OperationChanged += OnOperationChanged;
    }

    /// <summary>The rows matching <see cref="Filter"/>.</summary>
    public ObservableCollection<PackageRowViewModel> Packages { get; } = [];

    /// <summary>Every installed package, before filtering.</summary>
    public int TotalCount => _rows.Count;

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNone))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNone))]
    public partial bool HasLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    public bool ShowNone => HasLoaded && !IsBusy && Packages.Count == 0;

    /// <summary>Loads the list the first time the page is shown.</summary>
    public void EnsureLoaded()
    {
        if (!HasLoaded && !IsBusy)
        {
            _ = RefreshCommand.ExecuteAsync(null);
        }
    }

    public override void OnLanguageChanged()
    {
        ErrorMessage = DescribeErrors(_registry, _lastErrors);
        BuildRows(); // the "Local" source name is translated
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _registry.GetInstalledAsync(CancellationToken.None);
            _packages = result.Packages;
            _lastErrors = result.Errors;
            ErrorMessage = DescribeErrors(_registry, _lastErrors);
            BuildRows();
            HasLoaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildRows()
    {
        var sourceNames = _registry.Sources.ToDictionary(s => $"{s.ManagerId}/{s.Id}", s => s.DisplayName, StringComparer.Ordinal);
        var queued = _rows.Where(r => r.IsQueued).Select(r => r.Package.Key).ToHashSet(StringComparer.Ordinal);
        _rows.Clear();
        foreach (var package in _packages.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var capabilities = _registry.Find(package.ManagerId)?.Capabilities ?? ManagerCapabilities.None;
            var sourceName = package.SourceId == PackageSource.LocalId
                ? Loc["Source_Local"]
                : sourceNames.GetValueOrDefault($"{package.ManagerId}/{package.SourceId}", package.SourceId);
            _rows.Add(new PackageRowViewModel(package, sourceName, capabilities.HasFlag(ManagerCapabilities.Uninstall), UninstallAsync)
            {
                DetailsHandler = capabilities.HasFlag(ManagerCapabilities.Details) ? ShowDetailsAsync : null,
                IsQueued = queued.Contains(package.Key),
            });
        }

        OnPropertyChanged(nameof(TotalCount));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        Packages.Clear();
        foreach (var row in _rows.Where(r => filter.Length == 0
            || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || r.Id.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            Packages.Add(row);
        }
    }

    // Asks first: cancelling queues nothing.
    private async Task UninstallAsync(PackageRowViewModel row)
    {
        if (row.IsQueued || !await _confirmer.ConfirmUninstallAsync(row.Package))
        {
            return;
        }

        row.IsQueued = true;
        var result = await _actions.RequestAsync([row.Package], OperationKind.Uninstall);
        row.IsQueued = result.Queued.Count > 0;
    }

    private Task ShowDetailsAsync(PackageRowViewModel row) => _details.ShowAsync(
        row,
        DetailsContext.Installed,
        new PackageDetailsActions(
            Primary: row.IsQueued ? null : _ => UninstallAsync(row),
            Ignore: _ignored.IsIgnored(row.Package) ? null : p => _ignored.Ignore(p),
            InstallVersion: (p, version) => _actions.RequestAsync([p], OperationKind.Install, version)));

    // A successful uninstall removes the row; raised on the queue's thread.
    private void OnOperationChanged(object? sender, QueuedOperation operation)
    {
        if (operation.Kind != OperationKind.Uninstall || operation.State is not (OperationState.Succeeded or OperationState.Failed or OperationState.Cancelled))
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            var row = _rows.FirstOrDefault(r => r.Package.Key == operation.Package.Key);
            if (row is null)
            {
                return;
            }

            if (operation.State == OperationState.Succeeded)
            {
                _packages = [.. _packages.Where(p => p.Key != operation.Package.Key)];
                _rows.Remove(row);
                Packages.Remove(row);
                OnPropertyChanged(nameof(TotalCount));
                LogRowRemoved(operation.Package.Key);
            }
            else
            {
                row.IsQueued = false;
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Uninstall of {PackageKey} succeeded: row removed")]
    private partial void LogRowRemoved(string packageKey);
}
