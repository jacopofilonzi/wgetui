using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.App.Localization;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>UI-thread mirror of one <see cref="QueuedOperation"/>; <see cref="Refresh"/> copies its state.</summary>
public sealed partial class OperationItemViewModel : ObservableObject
{
    private readonly OperationQueue _queue;

    public OperationItemViewModel(QueuedOperation operation, OperationQueue queue, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(operation);
        SourceName = sourceName;
        Operation = operation;
        _queue = queue;
        Refresh();
    }

    public QueuedOperation Operation { get; }

    public string Name => Operation.Package.Name;

    public string Id => Operation.Package.Id;

    public string SourceId => Operation.Package.SourceId;

    public string SourceName { get; }

    public string KindText => Loc.Instance[Operation.Kind == OperationKind.Install ? "Ops_Kind_Install" : "Ops_Kind_Upgrade"];

    public OperationState State { get; private set; }

    public string StateText => Loc.Instance["Ops_State_" + State];

    public bool IsRunning => State == OperationState.Running;

    public bool IsFailed => State == OperationState.Failed;

    public bool IsSucceeded => State == OperationState.Succeeded;

    public double Percent { get; private set; }

    public bool IsIndeterminate { get; private set; }

    public string? StageText { get; private set; }

    /// <summary>"42%" while running with a known percentage, else null.</summary>
    public string? PercentText { get; private set; }

    public bool HasPercent => PercentText is not null;

    public string? ErrorText { get; private set; }

    public bool HasError => ErrorText is not null;

    public bool RebootRequired { get; private set; }

    public bool CanCancel { get; private set; }

    public void Refresh()
    {
        State = Operation.State;
        var progress = Operation.Progress;
        Percent = progress?.Percent ?? 0;
        IsIndeterminate = IsRunning && progress?.Percent is null;
        StageText = IsRunning && progress is { } p ? Loc.Instance["Ops_Stage_" + p.Stage] : null;
        PercentText = IsRunning && progress?.Percent is { } pct ? string.Format(Loc.Instance.Culture, "{0:0}%", pct) : null;
        var result = Operation.Result;
        ErrorText = State == OperationState.Failed ? Loc.Instance.Error(result?.ErrorCode) : null;
        RebootRequired = result?.RebootRequired ?? false;
        CanCancel = Operation.CanCancel;
        OnPropertyChanged(string.Empty);
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _queue.Cancel(Operation);
}
