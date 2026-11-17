using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.Core.Operations;

/// <summary>
/// Single sequential queue of installs, upgrades and uninstalls: one operation runs at a time, in enqueue order.
/// </summary>
/// <remarks>
/// Sequential on purpose: installers fight over locks (MSI, the winget server) and the user expects
/// "upgrade all" to go one by one. A failed or cancelled operation never stops the queue.
/// <see cref="OperationChanged"/> and <see cref="OperationAdded"/> are raised on the worker or caller
/// thread: UI subscribers must marshal to their own thread.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is a queue of operations; the name is part of the documented architecture.")]
public sealed partial class OperationQueue : IAsyncDisposable
{
    private readonly ManagerRegistry _registry;
    private readonly ILogger<OperationQueue> _logger;
    private readonly Channel<QueuedOperation> _channel = Channel.CreateUnbounded<QueuedOperation>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _gate = new();
    private readonly List<QueuedOperation> _operations = [];
    private readonly IReadOnlyList<IOperationHook> _hooks;
    private readonly Task _worker;
    private long _nextSequence;

    public OperationQueue(ManagerRegistry registry, ILogger<OperationQueue> logger)
        : this(registry, [], logger)
    {
    }

    public OperationQueue(ManagerRegistry registry, IEnumerable<IOperationHook> hooks, ILogger<OperationQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        _registry = registry;
        _logger = logger;
        _hooks = [.. hooks];
        _worker = Task.Run(RunAsync);
    }

    public event EventHandler<QueuedOperation>? OperationAdded;

    public event EventHandler<QueuedOperation>? OperationChanged;

    /// <summary>Snapshot of every operation of the session, in enqueue order.</summary>
    public IReadOnlyList<QueuedOperation> Operations
    {
        get
        {
            lock (_gate)
            {
                return [.. _operations];
            }
        }
    }

    /// <summary>
    /// Queues an operation. If the same package already has an active (queued or running) operation of the
    /// same kind, that one is returned instead of queuing a duplicate.
    /// </summary>
    public QueuedOperation Enqueue(Package package, OperationKind kind, OperationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        options ??= OperationOptions.None;
        QueuedOperation operation;
        lock (_gate)
        {
            var existing = _operations.FirstOrDefault(o => !o.IsFinished && o.Kind == kind && o.Package.Key == package.Key);
            if (existing is not null)
            {
                return existing;
            }

            var supportsCancel = _registry.Find(package.ManagerId)?.Capabilities.HasFlag(ManagerCapabilities.Cancel) ?? false;
            operation = new QueuedOperation(++_nextSequence, package, kind, options, supportsCancel);
            _operations.Add(operation);
            if (!_channel.Writer.TryWrite(operation))
            {
                throw new InvalidOperationException("The operation queue is shut down.");
            }
        }

        LogEnqueued(operation.Sequence, kind, package.Key);
        OperationAdded?.Invoke(this, operation);
        return operation;
    }

    /// <summary>Queues several operations of the same kind in order (e.g. "upgrade all").</summary>
    public IReadOnlyList<QueuedOperation> EnqueueMany(IEnumerable<Package> packages, OperationKind kind, OperationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return [.. packages.Select(p => Enqueue(p, kind, options))];
    }

    /// <summary>Cancels a queued operation, or asks a running one to stop if its manager supports it.</summary>
    public void Cancel(QueuedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var cancelledQueued = operation.RequestCancel(out var signalledRunning);
        if (cancelledQueued)
        {
            LogCancelled(operation.Sequence, "queued");
            OperationChanged?.Invoke(this, operation);
        }
        else if (signalledRunning)
        {
            LogCancelled(operation.Sequence, "running");
            OperationChanged?.Invoke(this, operation);
        }
    }

    /// <summary>Removes finished operations from <see cref="Operations"/>.</summary>
    public IReadOnlyList<QueuedOperation> ClearFinished()
    {
        lock (_gate)
        {
            var finished = _operations.Where(o => o.IsFinished).ToList();
            _operations.RemoveAll(o => o.IsFinished);
            return finished;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: shutdown cancels the worker.
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync()
    {
        await foreach (var operation in _channel.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            await ExecuteAsync(operation).ConfigureAwait(false);
        }
    }

    private async Task ExecuteAsync(QueuedOperation operation)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        if (!operation.TryStart(runCancellation))
        {
            return; // Cancelled while queued.
        }

        OperationChanged?.Invoke(this, operation);
        LogStarted(operation.Sequence, operation.Kind, operation.Package.Key);
        await RunHooksBeforeAsync(operation, runCancellation.Token).ConfigureAwait(false);

        var result = await RunManagerAsync(operation, runCancellation.Token).ConfigureAwait(false);
        operation.Finish(result);
        if (result.Succeeded)
        {
            LogSucceeded(operation.Sequence, operation.Package.Key, result.RebootRequired);
        }
        else
        {
            LogFailed(operation.Sequence, operation.Package.Key, result.ErrorCode, result.Message);
        }

        OperationChanged?.Invoke(this, operation);
        RunHooksAfter(operation, result);
    }

    private async Task RunHooksBeforeAsync(QueuedOperation operation, CancellationToken cancellationToken)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                await hook.BeforeAsync(operation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A hook is an add-on: it never stops the operation.
                LogHookFailed(ex, hook.GetType().Name, operation.Sequence);
            }
        }
    }

    private void RunHooksAfter(QueuedOperation operation, OperationResult result)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                hook.After(operation, result);
            }
            catch (Exception ex)
            {
                LogHookFailed(ex, hook.GetType().Name, operation.Sequence);
            }
        }
    }

    private async Task<OperationResult> RunManagerAsync(QueuedOperation operation, CancellationToken cancellationToken)
    {
        var manager = _registry.Find(operation.Package.ManagerId);
        if (manager is null)
        {
            return OperationResult.Failure(ErrorCodes.ManagerNotFound, $"No package manager '{operation.Package.ManagerId}' is registered.");
        }

        var required = operation.Kind switch
        {
            OperationKind.Install => operation.Options.TargetVersion is null ? ManagerCapabilities.Install : ManagerCapabilities.Install | ManagerCapabilities.InstallVersion,
            OperationKind.Upgrade => ManagerCapabilities.Upgrade,
            _ => ManagerCapabilities.Uninstall,
        };
        if ((manager.Capabilities & required) != required)
        {
            return OperationResult.Failure(ErrorCodes.NotSupported, $"{manager.Id} does not support {operation.Kind}.");
        }

        var progress = new ProgressSink(this, operation);
        try
        {
            var result = operation.Kind switch
            {
                OperationKind.Install => await manager.InstallAsync(operation.Package, operation.Options, progress, cancellationToken).ConfigureAwait(false),
                OperationKind.Upgrade => await manager.UpgradeAsync(operation.Package, operation.Options, progress, cancellationToken).ConfigureAwait(false),
                _ => await manager.UninstallAsync(operation.Package, operation.Options, progress, cancellationToken).ConfigureAwait(false),
            };
            return result.Succeeded || !operation.WasCancelRequested ? result : OperationResult.Cancelled();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancelled();
        }
        catch (PackageManagerException ex)
        {
            return OperationResult.Failure(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            // Contract says managers report failures as results; keep the queue alive if one does not.
            LogUnexpected(ex, operation.Sequence, operation.Package.Key);
            return OperationResult.Failure(ErrorCodes.Unexpected, $"{operation.Kind} of {operation.Package.Key} threw: {ex.Message}");
        }
    }

    // Not Progress<T>: that would capture the caller's SynchronizationContext. Updates go straight to the
    // operation and subscribers marshal themselves.
    private sealed class ProgressSink(OperationQueue queue, QueuedOperation operation) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            operation.ReportProgress(value);
            queue.OperationChanged?.Invoke(queue, operation);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued operation #{Sequence}: {Kind} {PackageKey}")]
    private partial void LogEnqueued(long sequence, OperationKind kind, string packageKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting operation #{Sequence}: {Kind} {PackageKey}")]
    private partial void LogStarted(long sequence, OperationKind kind, string packageKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Operation #{Sequence} {PackageKey} succeeded (reboot required: {RebootRequired})")]
    private partial void LogSucceeded(long sequence, string packageKey, bool rebootRequired);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation #{Sequence} {PackageKey} did not succeed: {ErrorCode} {Detail}")]
    private partial void LogFailed(long sequence, string packageKey, string? errorCode, string? detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancel requested for {State} operation #{Sequence}")]
    private partial void LogCancelled(long sequence, string state);

    [LoggerMessage(Level = LogLevel.Error, Message = "Operation hook {Hook} failed for operation #{Sequence}")]
    private partial void LogHookFailed(Exception ex, string hook, long sequence);

    [LoggerMessage(Level = LogLevel.Error, Message = "Operation #{Sequence} {PackageKey} threw unexpectedly")]
    private partial void LogUnexpected(Exception ex, long sequence, string packageKey);
}
