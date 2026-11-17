using Microsoft.Extensions.Logging;
using WgetUi.App.Localization;
using WgetUi.App.Platform;
using WgetUi.App.Services;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>
/// Tells the user when an installer is waiting on the UAC prompt, which otherwise sits unnoticed behind other
/// windows while the queue looks stuck.
/// </summary>
/// <remarks>
/// winget does not report "waiting for elevation", so this watches for the UAC process (consent.exe) from the
/// moment an operation reaches the Installing stage, every 500 ms for 20 s. If it appears in that window: a
/// toast naming the package, a flashing taskbar button and the orange tray dot until consent.exe is gone.
/// After 20 s without a prompt the operation is no longer watched (an unrelated UAC prompt later is not ours).
/// <see cref="Poll"/> takes the time as an argument so tests drive it without waiting.
/// </remarks>
public sealed partial class AdminPromptWatcher : IDisposable
{
    public const string ConsentProcess = "consent";
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan WatchWindow = TimeSpan.FromSeconds(20);

    private readonly TrayViewModel _tray;
    private readonly IProcessSource _processes;
    private readonly IToastNotifier _notifier;
    private readonly IWindowFlasher _flasher;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ILogger<AdminPromptWatcher> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<long, WatchedOperation> _watches = [];
    private readonly HashSet<long> _seen = [];
    private bool _promptPending;
    private ITimer? _timer;

    public AdminPromptWatcher(OperationQueue queue, TrayViewModel tray, IProcessSource processes, IToastNotifier notifier, IWindowFlasher flasher, IUiDispatcher dispatcher, TimeProvider time, ILogger<AdminPromptWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _tray = tray;
        _processes = processes;
        _notifier = notifier;
        _flasher = flasher;
        _dispatcher = dispatcher;
        _time = time;
        _logger = logger;
        queue.OperationChanged += (_, operation) => OnOperationChanged(operation);
    }

    /// <summary>The orange state: a prompt was detected and consent.exe is still running.</summary>
    public bool IsPromptPending
    {
        get
        {
            lock (_gate)
            {
                return _promptPending;
            }
        }
    }

    /// <summary>Starts watching an operation that just reached the Installing stage.</summary>
    public void Watch(long sequence, string packageName, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_seen.Add(sequence))
            {
                return; // Already watched once: progress reports repeat the Installing stage.
            }

            _watches[sequence] = new WatchedOperation(packageName, now);
            _timer ??= _time.CreateTimer(_ => Poll(_time.GetUtcNow()), null, PollInterval, PollInterval);
        }

        LogWatching(sequence);
    }

    /// <summary>One check: expires old watches, raises the notice, clears it when the prompt is gone.</summary>
    public void Poll(DateTimeOffset now)
    {
        string? notify = null;
        bool? pendingChanged = null;
        lock (_gate)
        {
            foreach (var (sequence, watch) in _watches.ToList())
            {
                if (now - watch.Started > WatchWindow)
                {
                    _watches.Remove(sequence);
                }
            }

            if (_watches.Count == 0 && !_promptPending)
            {
                StopTimer();
                return;
            }

            var consent = _processes.IsRunning(ConsentProcess);
            if (consent && !_promptPending && _watches.Count > 0)
            {
                // Notify once per operation: the first watched one is the one installing (the queue is sequential).
                var (sequence, watch) = _watches.First();
                _watches.Remove(sequence);
                notify = watch.PackageName;
                _promptPending = true;
                pendingChanged = true;
            }
            else if (!consent && _promptPending)
            {
                _promptPending = false;
                pendingChanged = false;
            }
        }

        if (notify is not null)
        {
            LogPromptDetected(notify);
            _notifier.Show(Loc.Instance.Format("Toast_AdminPrompt_Title", notify), Loc.Instance["Toast_AdminPrompt_Body"]);
            _dispatcher.Post(_flasher.Flash);
        }

        if (pendingChanged is { } pending)
        {
            _dispatcher.Post(() => _tray.IsAdminPromptPending = pending);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopTimer();
        }
    }

    private void OnOperationChanged(QueuedOperation operation)
    {
        if (operation.State == OperationState.Running && operation.Progress?.Stage is OperationStage.Installing or OperationStage.Uninstalling)
        {
            Watch(operation.Sequence, operation.Package.Name, _time.GetUtcNow());
        }
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private sealed record WatchedOperation(string PackageName, DateTimeOffset Started);

    [LoggerMessage(Level = LogLevel.Information, Message = "Operation #{Sequence} is installing: watching for a UAC prompt")]
    private partial void LogWatching(long sequence);

    [LoggerMessage(Level = LogLevel.Information, Message = "UAC prompt detected while installing {PackageName}")]
    private partial void LogPromptDetected(string packageName);
}
