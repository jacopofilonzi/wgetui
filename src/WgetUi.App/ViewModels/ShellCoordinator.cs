using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Platform;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>What closing the main window does.</summary>
public enum CloseOutcome
{
    /// <summary>Keep running in the tray.</summary>
    Hide,

    /// <summary>Quit the app (confirmed, or nothing was pending).</summary>
    Quit,

    /// <summary>The user cancelled the quit confirmation.</summary>
    Stay,
}

/// <summary>Asks the user whether to quit while operations are queued or running.</summary>
public interface IQuitConfirmer
{
    Task<bool> ConfirmQuitAsync(int pendingCount);
}

/// <summary>
/// Window close and Quit decisions: close-to-tray (with a one-time hint), and a confirmation before quitting
/// with pending operations, which are cancelled when the user confirms. Testable; the App's shell host
/// applies the outcome to the window.
/// </summary>
public sealed partial class ShellCoordinator
{
    private readonly SettingsService _settings;
    private readonly OperationQueue _queue;
    private readonly IQuitConfirmer _confirmer;
    private readonly IToastNotifier _notifier;
    private readonly ILogger<ShellCoordinator> _logger;

    public ShellCoordinator(SettingsService settings, OperationQueue queue, IQuitConfirmer confirmer, IToastNotifier notifier, ILogger<ShellCoordinator> logger)
    {
        _settings = settings;
        _queue = queue;
        _confirmer = confirmer;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<CloseOutcome> OnCloseRequestedAsync()
    {
        if (!_settings.Current.CloseToTray)
        {
            return await TryQuitAsync().ConfigureAwait(true) ? CloseOutcome.Quit : CloseOutcome.Stay;
        }

        if (!_settings.Current.TrayHintShown)
        {
            _notifier.Show(Loc.Instance["Toast_TrayHint_Title"], Loc.Instance["Toast_TrayHint_Body"]);
            _settings.Update(s => s.TrayHintShown = true);
        }

        return CloseOutcome.Hide;
    }

    /// <summary>True when the app may quit now. Pending operations need confirmation and are then cancelled.</summary>
    public async Task<bool> TryQuitAsync()
    {
        var pending = _queue.Operations.Where(o => !o.IsFinished).ToList();
        if (pending.Count > 0)
        {
            if (!await _confirmer.ConfirmQuitAsync(pending.Count).ConfigureAwait(true))
            {
                return false;
            }

            foreach (var operation in pending)
            {
                _queue.Cancel(operation);
            }

            LogQuitCancelled(pending.Count);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Quitting: cancelled {Count} pending operation(s)")]
    private partial void LogQuitCancelled(int count);
}
