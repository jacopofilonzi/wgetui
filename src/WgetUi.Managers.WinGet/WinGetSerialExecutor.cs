using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.Managers.WinGet;

/// <summary>
/// Runs every winget COM call one at a time. winget returns partial or empty results when a read runs
/// while an install is in progress, so reads and installs share one lock.
/// </summary>
/// <remarks>
/// Safety valves: a caller that cannot get the lock within <see cref="_acquireTimeout"/> fails with
/// <see cref="ErrorCodes.Timeout"/> instead of hanging, and a query that runs longer than
/// <see cref="_queryTimeout"/> fails its caller. The lock is still released only when the COM call
/// returns, so serialization is never broken by a timeout.
/// </remarks>
internal sealed class WinGetSerialExecutor : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeSpan _acquireTimeout;
    private readonly TimeSpan _queryTimeout;

    public WinGetSerialExecutor(TimeSpan acquireTimeout, TimeSpan queryTimeout)
    {
        _acquireTimeout = acquireTimeout;
        _queryTimeout = queryTimeout;
    }

    /// <summary>Runs a blocking COM query on a thread-pool thread under the lock.</summary>
    public async Task<T> RunQueryAsync<T>(string what, Func<T> query, CancellationToken cancellationToken)
    {
        await AcquireAsync(what, cancellationToken).ConfigureAwait(false);
        Task<T> work;
        try
        {
            work = Task.Run(query, CancellationToken.None);
        }
        catch
        {
            _lock.Release();
            throw;
        }

        // Release when the COM call really ends, even if the caller stopped waiting.
        _ = work.ContinueWith(_ => _lock.Release(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            return await work.WaitAsync(_queryTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new PackageManagerException(ErrorCodes.Timeout, $"winget {what} did not answer within {_queryTimeout.TotalSeconds:0} s.", ex);
        }
    }

    /// <summary>Runs a long operation (install, upgrade) under the lock for its whole duration.</summary>
    public async Task<T> RunOperationAsync<T>(string what, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await AcquireAsync(what, cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(operation, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task AcquireAsync(string what, CancellationToken cancellationToken)
    {
        if (!await _lock.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false))
        {
            throw new PackageManagerException(ErrorCodes.Timeout, $"winget is busy: {what} waited more than {_acquireTimeout.TotalSeconds:0} s for the previous call.");
        }
    }
}
