using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.App.Services;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>Search across every manager and source, filter by source, install.</summary>
public sealed partial class SearchViewModel : ViewModelBase
{
    private readonly ManagerRegistry _registry;
    private readonly PackageActions _actions;
    private List<PackageRowViewModel> _allResults = [];
    private IReadOnlyList<ManagerError> _lastErrors = [];

    public SearchViewModel(ManagerRegistry registry, PackageActions actions, OperationQueue queue, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _registry = registry;
        _actions = actions;
        Sources = [new SourceOption(null, null), .. registry.Sources.Select(s => new SourceOption(SourceKey(s.ManagerId, s.Id), s.DisplayName))];
        SelectedSource = Sources[0];

        // An install that did not succeed can be retried from its row.
        queue.OperationChanged += (_, operation) =>
        {
            if (operation.Kind == OperationKind.Install && operation.State is OperationState.Failed or OperationState.Cancelled)
            {
                dispatcher.Post(() =>
                {
                    foreach (var row in _allResults.Where(r => r.Package.Key == operation.Package.Key))
                    {
                        row.IsQueued = false;
                    }
                });
            }
        };
    }

    public ObservableCollection<SourceOption> Sources { get; }

    public ObservableCollection<PackageRowViewModel> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SourceOption SelectedSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults), nameof(ShowHint))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults), nameof(ShowHint))]
    public partial bool HasSearched { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Last action outcome shown under the list (e.g. agreements could not be read).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    public bool HasError => ErrorMessage is not null;

    public bool HasNotice => Notice is not null;

    public bool ShowNoResults => HasSearched && !IsBusy && Results.Count == 0;

    public bool ShowHint => !HasSearched && !IsBusy;

    public override void OnLanguageChanged()
    {
        foreach (var source in Sources)
        {
            source.Refresh();
        }

        ErrorMessage = DescribeErrors(_registry, _lastErrors);
    }

    partial void OnSelectedSourceChanged(SourceOption value) => ApplyFilter();

    private static string SourceKey(string managerId, string sourceId) => $"{managerId}/{sourceId}";

    private bool CanSearch() => !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanSearch), IncludeCancelCommand = true)]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Notice = null;
        try
        {
            var result = await _registry.SearchAsync(Query.Trim(), cancellationToken);
            var sourceNames = _registry.Sources.ToDictionary(s => SourceKey(s.ManagerId, s.Id), s => s.DisplayName, StringComparer.Ordinal);
            _allResults = [.. result.Packages.Select(p => new PackageRowViewModel(
                p,
                sourceNames.GetValueOrDefault(SourceKey(p.ManagerId, p.SourceId), p.SourceId),
                _registry.Find(p.ManagerId)?.Capabilities.HasFlag(ManagerCapabilities.Install) ?? false,
                InstallAsync))];
            _lastErrors = result.Errors;
            ErrorMessage = DescribeErrors(_registry, _lastErrors);
            HasSearched = true;
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Superseded by a new search or cancelled by the user: keep the previous results.
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAsync(PackageRowViewModel row)
    {
        row.IsQueued = true;
        var result = await _actions.RequestAsync([row.Package], OperationKind.Install);
        row.IsQueued = result.Queued.Count > 0;
        Notice = result.AgreementErrors.Count > 0 ? Loc["Agreements_ReadFailed"] : null;
    }

    private void ApplyFilter()
    {
        Results.Clear();
        foreach (var row in _allResults.Where(r => SelectedSource.SourceKey is null || SourceKey(r.Package.ManagerId, r.Package.SourceId) == SelectedSource.SourceKey))
        {
            Results.Add(row);
        }

        OnPropertyChanged(nameof(ShowNoResults));
    }
}
