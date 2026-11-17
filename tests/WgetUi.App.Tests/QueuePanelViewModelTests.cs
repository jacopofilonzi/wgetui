using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Localization;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class QueuePanelViewModelTests : IAsyncDisposable
{
    private readonly ScriptedManager _manager = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly OperationQueue _queue;
    private readonly OperationsViewModel _vm;

    public QueuePanelViewModelTests()
    {
        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        _queue = new OperationQueue(registry, NullLogger<OperationQueue>.Instance);
        _vm = new OperationsViewModel(_queue, registry, _dispatcher);
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Queue events arrive on the worker thread: drain the posted UI updates until the condition holds.
    private async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            _dispatcher.Drain();
            if (condition())
            {
                return;
            }

            await Task.Delay(25, Ct);
        }

        Assert.Fail("Condition not reached in time.");
    }

    [Fact]
    public void PanelIsHiddenWhileThereIsNothingToShow()
    {
        Assert.False(_vm.IsPanelVisible);
        Assert.Equal(0, _vm.PendingCount);
    }

    [Fact]
    public async Task QueuingOpensThePanelAndCountsPendingOperations()
    {
        _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Install);
        _queue.Enqueue(ScriptedManager.Package("b"), OperationKind.Upgrade);

        await UntilAsync(() => _vm.Items.Count == 2 && _vm.Items[0].State == OperationState.Running);

        Assert.True(_vm.IsPanelVisible);
        Assert.True(_vm.IsExpanded);
        Assert.Equal(2, _vm.PendingCount);
        Assert.True(_vm.Items[0].CanCancel);
        Assert.True(_vm.Items[1].CanCancel);
    }

    [Fact]
    public async Task CollapsedChoiceIsKeptWhenMoreOperationsArrive()
    {
        _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Install);
        await UntilAsync(() => _vm.Items.Count == 1);

        _vm.ToggleExpandedCommand.Execute(null);
        _queue.Enqueue(ScriptedManager.Package("b"), OperationKind.Install);
        await UntilAsync(() => _vm.Items.Count == 2);

        Assert.True(_vm.IsCollapsed);
        Assert.True(_vm.IsPanelVisible);
        Assert.Equal(2, _vm.PendingCount);
        _vm.ToggleExpandedCommand.Execute(null);
        Assert.True(_vm.IsExpanded);
    }

    [Fact]
    public async Task FinishedResultsStayVisibleUntilClearedThenThePanelHides()
    {
        _queue.Enqueue(ScriptedManager.Package("ok"), OperationKind.Upgrade);
        _queue.Enqueue(ScriptedManager.Package("ko"), OperationKind.Upgrade);
        _manager.Complete("ok", OperationResult.Success());
        _manager.Complete("ko", OperationResult.Failure(ErrorCodes.InstallFailed, "exit code 1603"));

        await UntilAsync(() => _vm.Items.Count == 2 && _vm.Items.All(i => i.Operation.IsFinished) && _vm.PendingCount == 0);

        Assert.True(_vm.IsPanelVisible);
        Assert.True(_vm.HasFinished);
        var failed = _vm.Items.Single(i => i.Id == "ko");
        Assert.True(failed.HasError);
        Assert.Equal(Loc.Instance.Error(ErrorCodes.InstallFailed), failed.ErrorText);
        Assert.False(_vm.Items.Single(i => i.Id == "ok").HasError);

        _vm.ClearFinishedCommand.Execute(null);

        Assert.Empty(_vm.Items);
        Assert.False(_vm.IsPanelVisible);
    }

    [Fact]
    public async Task ClearKeepsPendingOperations()
    {
        _queue.Enqueue(ScriptedManager.Package("done"), OperationKind.Install);
        _manager.Complete("done", OperationResult.Success());
        await UntilAsync(() => _vm.Items.Count == 1 && _vm.Items[0].IsSucceeded);
        _queue.Enqueue(ScriptedManager.Package("busy"), OperationKind.Install);
        await UntilAsync(() => _vm.Items.Count == 2 && _vm.Items[1].IsRunning);

        _vm.ClearFinishedCommand.Execute(null);

        Assert.Equal("busy", _vm.Items.Single().Id);
        Assert.True(_vm.IsPanelVisible);
        Assert.Equal(1, _vm.PendingCount);
    }

    [Fact]
    public async Task CancelFromTheRowCancelsTheOperation()
    {
        _queue.Enqueue(ScriptedManager.Package("a"), OperationKind.Install);
        _queue.Enqueue(ScriptedManager.Package("b"), OperationKind.Install);
        await UntilAsync(() => _vm.Items.Count == 2);

        _vm.Items[1].CancelCommand.Execute(null);
        _vm.Items[0].CancelCommand.Execute(null);

        await UntilAsync(() => _vm.Items.All(i => i.State == OperationState.Cancelled));
        Assert.Equal(0, _vm.PendingCount);
        Assert.All(_vm.Items, i => Assert.False(i.CanCancel));
    }
}
