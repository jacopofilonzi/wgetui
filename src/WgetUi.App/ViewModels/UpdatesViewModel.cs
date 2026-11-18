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

/// <summary>Installed packages with an update: upgrade one, upgrade all, refresh, ignore a version.</summary>
/// <remarks>
/// Ignored updates (<see cref="IgnoredUpdatesService"/>) stay in <see cref="_rows"/> but never count: not in
/// <see cref="Count"/> (badge, tray), "Upgrade all" or <see cref="Loaded"/> (notifications). They are listed,
/// greyed, only while <see cref="ShowIgnored"/> is on.
/// </remarks>
public sealed partial class UpdatesViewModel : ViewModelBase
{
    private readonly ManagerRegistry _registry;
    private readonly PackageActions _actions;
    private readonly IgnoredUpdatesService _ignored;
    private readonly PackageDetailsLauncher _details;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<UpdatesViewModel> _logger;
    private readonly List<PackageRowViewModel> _rows = [];
    private IReadOnlyList<ManagerError> _lastErrors = [];

    public UpdatesViewModel(ManagerRegistry registry, PackageActions actions, IgnoredUpdatesService ignored, PackageDetailsLauncher details, OperationQueue queue, IUiDispatcher dispatcher, ILogger<UpdatesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(ignored);
        _registry = registry;
        _actions = actions;
        _ignored = ignored;
        _details = details;
        _dispatcher = dispatcher;
        _logger = logger;
        ShowIncludeUnknownToggle = registry.Managers.Any(m => m.Capabilities.HasFlag(ManagerCapabilities.IncludeUnknownVersions));
        Upgrades.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNone));
        queue.OperationChanged += OnOperationChanged;
        ignored.Changed += (_, _) => ApplyIgnored();
    }

    /// <summary>Raised on the UI thread after each refresh, with the (not ignored) packages the managers returned.</summary>
    public event EventHandler<UpgradesLoadedEventArgs>? Loaded;

    /// <summary>The rows on screen: pending updates, plus the ignored ones while <see cref="ShowIgnored"/> is on.</summary>
    public ObservableCollection<PackageRowViewModel> Upgrades { get; } = [];

    /// <summary>Pending updates that are not ignored (navigation badge, tray).</summary>
    public int Count => _rows.Count(r => !r.IsIgnored);

    public int IgnoredCount => _rows.Count(r => r.IsIgnored);

    /// <summary>The "Show ignored" toggle is offered while there is something to show (or it is on).</summary>
    public bool ShowIgnoredToggle => IgnoredCount > 0 || ShowIgnored;

    public string ShowIgnoredLabel => Loc.Format("Updates_ShowIgnored", IgnoredCount);

    /// <summary>Only when some manager can list packages with an unknown installed version.</summary>
    public bool ShowIncludeUnknownToggle { get; }

    [ObservableProperty]
    public partial bool IncludeUnknown { get; set; }

    [ObservableProperty]
    public partial bool ShowIgnored { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNone))]
    [NotifyCanExecuteChangedFor(nameof(UpgradeAllCommand), nameof(RefreshCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNone))]
    public partial bool HasLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    public bool HasError => ErrorMessage is not null;

    public bool HasNotice => Notice is not null;

    public bool ShowNone => HasLoaded && !IsBusy && Upgrades.Count == 0;

    /// <summary>The updates the last refresh found that are not ignored and can be upgraded (rows of finished upgrades are gone).</summary>
    public IReadOnlyList<Package> PendingUpdates => [.. _rows.Where(r => r.CanAct && !r.IsIgnored).Select(r => r.Package)];

    /// <summary>The last refresh had no manager failing (its list is the whole truth).</summary>
    public bool LastRefreshComplete { get; private set; }


    public override void OnLanguageChanged()
    {
        ErrorMessage = DescribeErrors(_registry, _lastErrors);
        OnPropertyChanged(nameof(ShowIgnoredLabel));
    }

    partial void OnIncludeUnknownChanged(bool value) => _ = RefreshCommand.ExecuteAsync(null);

    partial void OnShowIgnoredChanged(bool value) => RebuildVisible();

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        Notice = null;
        try
        {
            var result = await _registry.GetUpgradesAsync(IncludeUnknown, CancellationToken.None);
            var isComplete = result.Errors.Count == 0;
            var filtered = _ignored.Apply(result.Packages, isComplete);
            var sourceNames = _registry.Sources.ToDictionary(s => $"{s.ManagerId}/{s.Id}", s => s.DisplayName, StringComparer.Ordinal);
            _rows.Clear();
            foreach (var package in result.Packages.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var capabilities = _registry.Find(package.ManagerId)?.Capabilities ?? ManagerCapabilities.None;
                _rows.Add(new PackageRowViewModel(
                    package,
                    sourceNames.GetValueOrDefault($"{package.ManagerId}/{package.SourceId}", package.SourceId),
                    capabilities.HasFlag(ManagerCapabilities.Upgrade),
                    UpgradeAsync)
                {
                    DetailsHandler = capabilities.HasFlag(ManagerCapabilities.Details) ? ShowDetailsAsync : null,
                    IgnoreHandler = row => _ignored.Ignore(row.Package),
                    UnignoreHandler = Unignore,
                    IsIgnored = filtered.Ignored.Contains(package),
                });
            }

            RebuildVisible();
            _lastErrors = result.Errors;
            ErrorMessage = DescribeErrors(_registry, _lastErrors);
            LastRefreshComplete = isComplete;
            HasLoaded = true;
            Loaded?.Invoke(this, new UpgradesLoadedEventArgs(filtered.Visible, isComplete));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ignores the update of <paramref name="package"/> (from the details popup).</summary>
    public void Ignore(Package package) => _ignored.Ignore(package);

    /// <summary>Upgrades one package (from the details popup).</summary>
    public Task UpgradeAsync(Package package)
    {
        var row = _rows.FirstOrDefault(r => r.Package.Key == package.Key);
        return row is null ? Task.CompletedTask : RequestAsync([row]);
    }

    private Task ShowDetailsAsync(PackageRowViewModel row) => _details.ShowAsync(
        row,
        DetailsContext.Updates,
        new PackageDetailsActions(
            Primary: UpgradeAsync,
            Ignore: row.IsIgnored ? null : p => _ignored.Ignore(p),
            InstallVersion: async (p, version) => Notice = (await _actions.RequestAsync([p], OperationKind.Install, version)).AgreementErrors.Count > 0 ? Loc["Agreements_ReadFailed"] : null));

    private void Unignore(PackageRowViewModel row)
    {
        foreach (var record in _ignored.Records.Where(r => r.Matches(row.Package)).ToList())
        {
            _ignored.Restore(record);
        }
    }

    private bool CanUpgradeAll() => !IsBusy && _rows.Any(r => r.CanAct && !r.IsQueued && !r.IsIgnored);

    [RelayCommand(CanExecute = nameof(CanUpgradeAll))]
    private async Task UpgradeAllAsync()
    {
        var rows = _rows.Where(r => r.CanAct && !r.IsQueued && !r.IsIgnored).ToList();
        await RequestAsync(rows);
    }

    private Task<OperationRequestResult> UpgradeAsync(PackageRowViewModel row) => RequestAsync([row]);

    /// <summary>
    /// Automatic updates: queues these upgrades exactly like an Upgrade click (rows marked queued, agreements as
    /// for manual upgrades) and returns what was queued.
    /// </summary>
    public async Task<IReadOnlyList<QueuedOperation>> UpgradeAutomaticallyAsync(IReadOnlyList<Package> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var keys = packages.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var rows = _rows.Where(r => keys.Contains(r.Package.Key) && r.CanAct && !r.IsQueued).ToList();
        return rows.Count == 0 ? [] : (await RequestAsync(rows)).Queued;
    }

    private async Task<OperationRequestResult> RequestAsync(IReadOnlyList<PackageRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            row.IsQueued = true;
        }

        UpgradeAllCommand.NotifyCanExecuteChanged();
        var result = await _actions.RequestAsync([.. rows.Select(r => r.Package)], OperationKind.Upgrade);
        if (result.Declined)
        {
            foreach (var row in rows)
            {
                row.IsQueued = false;
            }
        }

        Notice = result.AgreementErrors.Count > 0 ? Loc["Agreements_ReadFailed"] : null;
        UpgradeAllCommand.NotifyCanExecuteChanged();
        return result;
    }

    /// <summary>Removes the update row of a package that was uninstalled.</summary>
    public void RemovePackage(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_rows.RemoveAll(r => string.Equals(r.Package.Key, package.Key, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            RebuildVisible();
        }
    }

    // Re-reads the records (ignored, restored or pruned) without asking the managers again.
    private void ApplyIgnored()
    {
        foreach (var row in _rows)
        {
            row.IsIgnored = _ignored.IsIgnored(row.Package);
        }

        RebuildVisible();
    }

    private void RebuildVisible()
    {
        Upgrades.Clear();
        foreach (var row in _rows.Where(r => ShowIgnored || !r.IsIgnored))
        {
            Upgrades.Add(row);
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IgnoredCount));
        OnPropertyChanged(nameof(ShowIgnoredToggle));
        OnPropertyChanged(nameof(ShowIgnoredLabel));
        OnPropertyChanged(nameof(ShowNone));
        UpgradeAllCommand.NotifyCanExecuteChanged();
    }

    // A successful upgrade removes the row; raised on the queue's thread.
    private void OnOperationChanged(object? sender, QueuedOperation operation)
    {
        if (operation.State is not (OperationState.Succeeded or OperationState.Failed or OperationState.Cancelled))
        {
            return;
        }

        if (operation.Kind == OperationKind.Uninstall)
        {
            if (operation.State == OperationState.Succeeded)
            {
                _dispatcher.Post(() => RemovePackage(operation.Package));
            }

            return;
        }

        if (operation.Kind != OperationKind.Upgrade)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            var row = _rows.FirstOrDefault(r => r.Package.Key == operation.Package.Key);
            if (row is null)
            {
                LogRowNotFound(operation.Package.Key, operation.State);
                return;
            }

            if (operation.State == OperationState.Succeeded)
            {
                _rows.Remove(row);
                RebuildVisible();
                LogRowRemoved(operation.Package.Key, Count);
            }
            else
            {
                row.IsQueued = false;
                UpgradeAllCommand.NotifyCanExecuteChanged();
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Upgrade of {PackageKey} succeeded: row removed, {Remaining} update(s) left")]
    private partial void LogRowRemoved(string packageKey, int remaining);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upgrade of {PackageKey} finished as {State} but no row matches it in the Updates list")]
    private partial void LogRowNotFound(string packageKey, OperationState state);
}

/// <summary>Result of one Updates refresh.</summary>
/// <param name="packages">Packages with an update that is not ignored, from every manager that answered.</param>
/// <param name="isComplete">True when no manager failed (the list is the whole truth).</param>
public sealed class UpgradesLoadedEventArgs(IReadOnlyList<Package> packages, bool isComplete) : EventArgs
{
    public IReadOnlyList<Package> Packages { get; } = packages;

    public bool IsComplete { get; } = isComplete;
}
