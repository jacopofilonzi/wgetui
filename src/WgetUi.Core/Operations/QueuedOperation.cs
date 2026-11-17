using WgetUi.Core.Models;

namespace WgetUi.Core.Operations;

public enum OperationKind
{
    Install,
    Upgrade,
    Uninstall,
}

public enum OperationState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// One install or upgrade in the <see cref="OperationQueue"/>. Mutated only by the queue (from its worker
/// thread); readers get consistent values through the lock and are told about changes by
/// <see cref="OperationQueue.OperationChanged"/>.
/// </summary>
public sealed class QueuedOperation
{
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<OperationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OperationState _state = OperationState.Queued;
    private OperationProgress? _progress;
    private OperationResult? _result;
    private CancellationTokenSource? _runCancellation;
    private bool _cancelRequested;

    internal QueuedOperation(long sequence, Package package, OperationKind kind, OperationOptions options, bool canCancelWhileRunning)
    {
        Options = options;
        Sequence = sequence;
        Package = package;
        Kind = kind;
        CanCancelWhileRunning = canCancelWhileRunning;
    }

    /// <summary>Monotonic enqueue order, also a stable id within the session.</summary>
    public long Sequence { get; }

    public Package Package { get; }

    public OperationKind Kind { get; }

    /// <summary>What the user accepted for this operation.</summary>
    public OperationOptions Options { get; }

    /// <summary>The owning manager supports cancelling a running install/upgrade.</summary>
    public bool CanCancelWhileRunning { get; }

    public OperationState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public OperationProgress? Progress
    {
        get
        {
            lock (_gate)
            {
                return _progress;
            }
        }
    }

    public OperationResult? Result
    {
        get
        {
            lock (_gate)
            {
                return _result;
            }
        }
    }

    public bool IsFinished => State is OperationState.Succeeded or OperationState.Failed or OperationState.Cancelled;

    /// <summary>True while a cancel request would have an effect.</summary>
    public bool CanCancel
    {
        get
        {
            lock (_gate)
            {
                return !_cancelRequested
                    && (_state == OperationState.Queued || (_state == OperationState.Running && CanCancelWhileRunning));
            }
        }
    }

    /// <summary>Completes when the operation reaches a final state.</summary>
    public Task<OperationResult> Completion => _completion.Task;

    /// <summary>Requests cancellation. Returns true when the operation was queued and is now cancelled.</summary>
    internal bool RequestCancel(out bool signalledRunning)
    {
        signalledRunning = false;
        lock (_gate)
        {
            if (_state == OperationState.Queued)
            {
                _cancelRequested = true;
                FinishLocked(OperationState.Cancelled, OperationResult.Cancelled());
                return true;
            }

            if (_state == OperationState.Running && CanCancelWhileRunning && !_cancelRequested)
            {
                _cancelRequested = true;
                _runCancellation?.Cancel();
                signalledRunning = true;
            }

            return false;
        }
    }

    /// <summary>Moves a queued operation to running. Returns false when it was cancelled meanwhile.</summary>
    internal bool TryStart(CancellationTokenSource runCancellation)
    {
        lock (_gate)
        {
            if (_state != OperationState.Queued)
            {
                return false;
            }

            _state = OperationState.Running;
            _runCancellation = runCancellation;
            _progress = new OperationProgress(OperationStage.Preparing, null);
            return true;
        }
    }

    internal void ReportProgress(OperationProgress progress)
    {
        lock (_gate)
        {
            if (_state == OperationState.Running)
            {
                _progress = progress;
            }
        }
    }

    internal void Finish(OperationResult result)
    {
        lock (_gate)
        {
            var state = result.Succeeded
                ? OperationState.Succeeded
                : result.ErrorCode == ErrorCodes.Cancelled ? OperationState.Cancelled : OperationState.Failed;
            FinishLocked(state, result);
        }
    }

    internal bool WasCancelRequested
    {
        get
        {
            lock (_gate)
            {
                return _cancelRequested;
            }
        }
    }

    private void FinishLocked(OperationState state, OperationResult result)
    {
        _state = state;
        _result = result;
        _runCancellation = null;
        _completion.TrySetResult(result);
    }
}
