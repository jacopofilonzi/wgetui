using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.App.Services;
using WgetUi.Core;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>
/// The queue panel on the right of the shell. Always present: collapsed (a narrow strip with the pending count)
/// by default, expanded when the user clicks it. It expands by itself only when an operation is queued while the
/// history is empty; once there is history, the user's expanded/collapsed choice wins for the rest of the session
/// (collapsing after the first download means later downloads don't pop it open). Finished items stay until cleared.
/// </summary>
public sealed partial class OperationsViewModel : ViewModelBase
{
    private readonly OperationQueue _queue;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<string, string> _sourceNames;
    private bool _windowActive;

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
                if (Items.Count == 0)
                {
                    IsExpanded = true;
                }

                Items.Add(Create(operation));
                Changed();
            }
        });
        queue.OperationChanged += (_, operation) => _dispatcher.Post(() =>
        {
            var item = Items.FirstOrDefault(i => i.Operation == operation);
            var wasFailed = item?.State == OperationState.Failed;
            item?.Refresh();
            if (item is { State: OperationState.Failed } && !wasFailed && !IsBeingViewed)
            {
                HasUnseenFailure = true;
            }

            Changed();
        });
    }

    public ObservableCollection<OperationItemViewModel> Items { get; } = [];

    /// <summary>Queued or running operations: the badge of the collapsed panel.</summary>
    public int PendingCount => Items.Count(i => i.State is OperationState.Queued or OperationState.Running);

    public bool HasPending => PendingCount > 0;

    public bool HasFinished => Items.Any(i => i.Operation.IsFinished);

    /// <summary>Nothing queued, running or finished: the expanded panel shows a placeholder.</summary>
    public bool IsEmpty => Items.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    public partial bool IsExpanded { get; set; }

    public bool IsCollapsed => !IsExpanded;

    /// <summary>Queued or running operations exist and one of them is running.</summary>
    public bool IsRunning => Items.Any(i => i.State == OperationState.Running);

    /// <summary>
    /// An operation failed while the user was not looking at the panel. Drives the red tray dot; cleared when
    /// the panel is seen (window active with the panel expanded) or the finished items are cleared.
    /// </summary>
    [ObservableProperty]
    public partial bool HasUnseenFailure { get; set; }

    private bool IsBeingViewed => _windowActive && IsExpanded;

    /// <summary>The shell reports whether the main window is the active one.</summary>
    public void SetWindowActive(bool active)
    {
        _windowActive = active;
        if (IsBeingViewed)
        {
            HasUnseenFailure = false;
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && _windowActive)
        {
            HasUnseenFailure = false;
        }
    }

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
        HasUnseenFailure = false;
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
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsRunning));
    }
}
