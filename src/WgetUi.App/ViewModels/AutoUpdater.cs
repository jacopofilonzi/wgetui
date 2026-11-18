using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Platform;
using WgetUi.App.Services;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>
/// Automatic updates, run after the scheduled update checks (startup and hourly, see
/// <see cref="UpdateCheckScheduler"/>), never after a manual Refresh. Queues real upgrades.
/// </summary>
/// <remarks>
/// For each pending update of the Updates list (ignored versions are already out of it) whose package has a rule
/// (<see cref="AutoUpdateService"/>), while the global switch is on, and that is not already queued or running:
/// when the app is open (<see cref="OpenAppDetector"/>) and neither the rule nor the global "update open apps too"
/// forces it, the update is postponed to the next check; otherwise it is queued like an Upgrade click
/// (<see cref="UpdatesViewModel.UpgradeAutomaticallyAsync"/>: same agreements policy, same relaunch hook).
/// Notifications: one "postponed" toast per package@version (the set is persisted in
/// <see cref="AppSettings.PostponedUpdates"/> and kept only while the update is pending), and once a batch has
/// finished a summary of what succeeded and what failed (cancelled ones are not reported). These toasts are
/// shown even while the window is active: unlike "N new updates", nothing on screen says that wgetui postponed or
/// ran an upgrade on its own. Used from the UI thread.
/// </remarks>
public sealed partial class AutoUpdater
{
    private const int NamesInText = 3;
    private readonly UpdatesViewModel _updates;
    private readonly AutoUpdateService _rules;
    private readonly SettingsService _settings;
    private readonly OpenAppDetector _detector;
    private readonly OperationQueue _queue;
    private readonly IToastNotifier _notifier;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<AutoUpdater> _logger;
    private bool _running;

    public AutoUpdater(
        UpdatesViewModel updates,
        AutoUpdateService rules,
        SettingsService settings,
        OpenAppDetector detector,
        OperationQueue queue,
        IToastNotifier notifier,
        IUiDispatcher dispatcher,
        ILogger<AutoUpdater> logger)
    {
        _updates = updates;
        _rules = rules;
        _settings = settings;
        _detector = detector;
        _queue = queue;
        _notifier = notifier;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>Completes once the summary of the last automatic batch was shown (for tests).</summary>
    public Task LastReport { get; private set; } = Task.CompletedTask;

    public static string KeyOf(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return $"{package.Key}@{package.AvailableVersion}";
    }

    /// <summary>Acts on the updates the last refresh found. Returns the operations it queued.</summary>
    public async Task<IReadOnlyList<QueuedOperation>> RunAsync(CancellationToken cancellationToken)
    {
        if (_running || !_updates.HasLoaded || !_rules.IsGloballyEnabled || _rules.Rules.Count == 0)
        {
            return [];
        }

        _running = true;
        try
        {
            var updates = _updates.PendingUpdates;
            var active = _queue.Operations.Where(o => !o.IsFinished).Select(o => o.Package.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<Package> toQueue = [];
            List<Package> postponed = [];
            foreach (var package in updates)
            {
                if (package.AvailableVersion is null || _rules.Find(package) is not { } rule || active.Contains(package.Key))
                {
                    continue;
                }

                if (!rule.Force && !_rules.ForceOpenApps && await _detector.IsOpenAsync(package, cancellationToken))
                {
                    postponed.Add(package);
                    LogPostponed(package.Key, package.AvailableVersion);
                }
                else
                {
                    toQueue.Add(package);
                }
            }

            NotifyPostponed(updates, postponed, _updates.LastRefreshComplete);
            if (toQueue.Count == 0)
            {
                return [];
            }

            var queued = await _updates.UpgradeAutomaticallyAsync(toQueue);
            LogQueued(queued.Count);
            if (queued.Count > 0)
            {
                LastReport = ReportAsync(queued);
            }

            return queued;
        }
        finally
        {
            _running = false;
        }
    }

    private void NotifyPostponed(IReadOnlyList<Package> updates, IReadOnlyList<Package> postponed, bool isComplete)
    {
        var known = _settings.Current.PostponedUpdates.ToHashSet(StringComparer.Ordinal);
        var pending = updates.Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        var fresh = postponed.Where(p => !known.Contains(KeyOf(p))).ToList();

        // Forget the updates that are no longer pending (installed, or a newer version came); a manager that failed
        // returned nothing, so keep everything then.
        IReadOnlyList<string> next = [.. known.Where(k => !isComplete || pending.Contains(k)).Concat(fresh.Select(KeyOf)).Distinct(StringComparer.Ordinal)];
        if (!next.ToHashSet(StringComparer.Ordinal).SetEquals(known))
        {
            _settings.Update(s => s.PostponedUpdates = next);
        }

        if (fresh.Count == 0)
        {
            return;
        }

        var loc = Loc.Instance;
        if (fresh.Count == 1)
        {
            _notifier.Show(loc.Format("Toast_AutoPostponed_One", fresh[0].Name, fresh[0].AvailableVersion), loc["Toast_AutoPostponed_OneBody"], ToastAction.OpenUpdates);
        }
        else
        {
            _notifier.Show(loc.Format("Toast_AutoPostponed_Many", fresh.Count), loc.Format("Toast_AutoPostponed_ManyBody", Names(fresh)), ToastAction.OpenUpdates);
        }
    }

    private async Task ReportAsync(IReadOnlyList<QueuedOperation> operations)
    {
        await Task.WhenAll(operations.Select(o => o.Completion)).ConfigureAwait(false);
        var succeeded = operations.Where(o => o.State == OperationState.Succeeded).Select(o => o.Package).ToList();
        var failed = operations.Where(o => o.State == OperationState.Failed).Select(o => o.Package).ToList();
        LogFinished(succeeded.Count, failed.Count);
        _dispatcher.Post(() =>
        {
            var loc = Loc.Instance;
            if (succeeded.Count > 0)
            {
                _notifier.Show(loc.Format("Toast_AutoUpdated", Names(succeeded)), loc["Toast_AutoUpdatedBody"], ToastAction.OpenUpdates);
            }

            if (failed.Count == 1)
            {
                _notifier.Show(loc.Format("Toast_AutoFailed_One", failed[0].Name), loc["Toast_AutoFailedBody"], ToastAction.OpenUpdates);
            }
            else if (failed.Count > 1)
            {
                _notifier.Show(loc.Format("Toast_AutoFailed_Many", Names(failed)), loc["Toast_AutoFailedBody"], ToastAction.OpenUpdates);
            }
        });
    }

    private static string Names(List<Package> packages)
    {
        var names = string.Join(", ", packages.Take(NamesInText).Select(p => p.Name));
        return packages.Count > NamesInText ? Loc.Instance.Format("Toast_NewUpdates_More", names, packages.Count - NamesInText) : names;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic update of {PackageKey} {Version} postponed: the app is open")]
    private partial void LogPostponed(string packageKey, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued {Count} automatic update(s)")]
    private partial void LogQueued(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic updates finished: {Succeeded} succeeded, {Failed} failed")]
    private partial void LogFinished(int succeeded, int failed);
}
