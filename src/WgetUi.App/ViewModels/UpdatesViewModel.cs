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

/// <summary>Installed packages with an update: upgrade one, upgrade all, refresh.</summary>
public sealed partial class UpdatesViewModel : ViewModelBase
{
    private readonly ManagerRegistry _registry;
    private readonly PackageActions _actions;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<UpdatesViewModel> _logger;
    private IReadOnlyList<ManagerError> _lastErrors = [];

    public UpdatesViewModel(ManagerRegistry registry, PackageActions actions, OperationQueue queue, IUiDispatcher dispatcher, ILogger<UpdatesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _registry = registry;
        _actions = actions;
        _dispatcher = dispatcher;
        _logger = logger;
        ShowIncludeUnknownToggle = registry.Managers.Any(m => m.Capabilities.HasFlag(ManagerCapabilities.IncludeUnknownVersions));
        Upgrades.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(ShowNone));
            UpgradeAllCommand.NotifyCanExecuteChanged();
        };
        queue.OperationChanged += OnOperationChanged;
    }

    public ObservableCollection<PackageRowViewModel> Upgrades { get; } = [];

    public int Count => Upgrades.Count;

    /// <summary>Only when some manager can list packages with an unknown installed version.</summary>
    public bool ShowIncludeUnknownToggle { get; }

    [ObservableProperty]
    public partial bool IncludeUnknown { get; set; }

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

    public override void OnLanguageChanged() => ErrorMessage = DescribeErrors(_registry, _lastErrors);

    partial void OnIncludeUnknownChanged(bool value) => _ = RefreshCommand.ExecuteAsync(null);

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        Notice = null;
        try
        {
            var result = await _registry.GetUpgradesAsync(IncludeUnknown, CancellationToken.None);
            var sourceNames = _registry.Sources.ToDictionary(s => $"{s.ManagerId}/{s.Id}", s => s.DisplayName, StringComparer.Ordinal);
            Upgrades.Clear();
            foreach (var package in result.Packages.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Upgrades.Add(new PackageRowViewModel(
                    package,
                    sourceNames.GetValueOrDefault($"{package.ManagerId}/{package.SourceId}", package.SourceId),
                    _registry.Find(package.ManagerId)?.Capabilities.HasFlag(ManagerCapabilities.Upgrade) ?? false,
                    UpgradeAsync));
            }

            _lastErrors = result.Errors;
            ErrorMessage = DescribeErrors(_registry, _lastErrors);
            HasLoaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUpgradeAll() => !IsBusy && Upgrades.Any(r => r.CanAct && !r.IsQueued);

    [RelayCommand(CanExecute = nameof(CanUpgradeAll))]
    private async Task UpgradeAllAsync()
    {
        var rows = Upgrades.Where(r => r.CanAct && !r.IsQueued).ToList();
        await RequestAsync(rows);
    }

    private Task UpgradeAsync(PackageRowViewModel row) => RequestAsync([row]);

    private async Task RequestAsync(IReadOnlyList<PackageRowViewModel> rows)
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
    }

    // A successful upgrade removes the row; raised on the queue's thread.
    private void OnOperationChanged(object? sender, QueuedOperation operation)
    {
        if (operation.Kind != OperationKind.Upgrade || operation.State is not (OperationState.Succeeded or OperationState.Failed or OperationState.Cancelled))
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            var row = Upgrades.FirstOrDefault(r => r.Package.Key == operation.Package.Key);
            if (row is null)
            {
                LogRowNotFound(operation.Package.Key, operation.State);
                return;
            }

            if (operation.State == OperationState.Succeeded)
            {
                Upgrades.Remove(row);
                LogRowRemoved(operation.Package.Key, Upgrades.Count);
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
