using Microsoft.Extensions.Logging;
using WgetUi.App.Services;

namespace WgetUi.App.ViewModels;

/// <summary>Ticks at a fixed interval; behind an interface so tests drive the clock.</summary>
public interface IUpdateCheckTicker
{
    /// <summary>Waits for the next tick; false when the ticker was stopped.</summary>
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken);
}

/// <summary>Real ticker: every hour.</summary>
public sealed class HourlyTicker : IUpdateCheckTicker, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly PeriodicTimer _timer = new(Interval);

    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken) => _timer.WaitForNextTickAsync(cancellationToken);

    public void Dispose() => _timer.Dispose();
}

/// <summary>
/// Background update check: refreshes the Updates list (and so the tray dot and notifications) on every tick.
/// The startup check is the shell's first refresh. A tick is skipped while the list is already refreshing or
/// an operation is queued/running: winget serializes calls, so a check would wait behind the install and
/// time out.
/// </summary>
public sealed partial class UpdateCheckScheduler : IAsyncDisposable
{
    private readonly UpdatesViewModel _updates;
    private readonly OperationsViewModel _operations;
    private readonly IUpdateCheckTicker _ticker;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<UpdateCheckScheduler> _logger;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public UpdateCheckScheduler(UpdatesViewModel updates, OperationsViewModel operations, IUpdateCheckTicker ticker, IUiDispatcher dispatcher, ILogger<UpdateCheckScheduler> logger)
    {
        _updates = updates;
        _operations = operations;
        _ticker = ticker;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void Start() => _loop ??= Task.Run(RunAsync);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _stop.Dispose();
    }

    /// <summary>One tick, on the UI thread.</summary>
    public void Tick()
    {
        if (_operations.HasPending || !_updates.RefreshCommand.CanExecute(null))
        {
            LogSkipped();
            return;
        }

        LogChecking();
        _ = _updates.RefreshCommand.ExecuteAsync(null);
    }

    private async Task RunAsync()
    {
        while (await _ticker.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
        {
            _dispatcher.Post(Tick);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled update check")]
    private partial void LogChecking();

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled update check skipped: busy or operations pending")]
    private partial void LogSkipped();
}
