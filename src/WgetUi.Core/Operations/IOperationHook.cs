using WgetUi.Core.Models;

namespace WgetUi.Core.Operations;

/// <summary>
/// Extension point around each queued operation, run by the <see cref="OperationQueue"/> worker (e.g. the App
/// snapshots an app's processes before its upgrade and restarts them after). Hooks must not throw; if one
/// does, the queue logs it and goes on: a hook never fails or blocks an operation for long.
/// </summary>
public interface IOperationHook
{
    /// <summary>Right before the manager is called, on the worker (no other operation is running).</summary>
    Task BeforeAsync(QueuedOperation operation, CancellationToken cancellationToken);

    /// <summary>After the operation reached its final state. Long work must be started in the background.</summary>
    void After(QueuedOperation operation, OperationResult result);
}
