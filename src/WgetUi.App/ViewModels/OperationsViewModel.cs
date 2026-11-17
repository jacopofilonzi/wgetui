using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.App.Services;
using WgetUi.Core;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>
/// The queue panel on the right of the shell. Hidden while there is nothing to show; appears when an
/// operation is queued; stays visible after everything finished so results (and failures) are seen, until
/// the user clears them. Expanded/collapsed is the user's choice for the session.
/// </summary>
public sealed partial class OperationsViewModel : ViewModelBase
{
    private readonly OperationQueue _queue;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<string, string> _sourceNames;

    public OperationsViewModel(OperationQueue queue, ManagerRegistry registry, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(registry);
        _queue = queue;
        _dispatcher = dispatcher;
        _sourceNames = registry.Sources.ToDictionary(s => $"{s.ManagerId}/{s.Id}", s => s.DisplayName, StringComparer.Ordinal);
        foreach (var operation in queue.Operations)
        {
            Items.Add(Create(operation));
        }

        queue.OperationAdded += (_, operation) => _dispatcher.Post(() =>
        {
            if (Items.All(i => i.Operation != operation))
            {
                Items.Add(Create(operation));
                Changed();
            }
        });
        queue.OperationChanged += (_, operation) => _dispatcher.Post(() =>
        {
            Items.FirstOrDefault(i => i.Operation == operation)?.Refresh();
            Changed();
        });
    }

    public ObservableCollection<OperationItemViewModel> Items { get; } = [];

    /// <summary>Queued or running operations: the badge of the collapsed panel.</summary>
    public int PendingCount => Items.Count(i => i.State is OperationState.Queued or OperationState.Running);

    public bool HasPending => PendingCount > 0;

    public bool HasFinished => Items.Any(i => i.Operation.IsFinished);

    /// <summary>The panel exists only while it has something to show.</summary>
    public bool IsPanelVisible => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    public partial bool IsExpanded { get; set; } = true;

    public bool IsCollapsed => !IsExpanded;

    public override void OnLanguageChanged()
    {
        foreach (var item in Items)
        {
            item.Refresh();
        }
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void ClearFinished()
    {
        var removed = _queue.ClearFinished();
        foreach (var item in Items.Where(i => removed.Contains(i.Operation)).ToList())
        {
            Items.Remove(item);
        }

        Changed();
    }

    private OperationItemViewModel Create(QueuedOperation operation) =>
        new(operation, _queue, _sourceNames.GetValueOrDefault($"{operation.Package.ManagerId}/{operation.Package.SourceId}", operation.Package.SourceId));

    private void Changed()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(IsPanelVisible));
    }
}
