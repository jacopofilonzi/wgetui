using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.App.Services;

/// <summary>
/// The per-app "update automatically" rules, persisted in settings.json (<see cref="AppSettings.AutoUpdates"/>),
/// plus the two global switches (<see cref="AppSettings.AutoUpdatesEnabled"/>, <see cref="AppSettings.AutoUpdateOpenApps"/>).
/// Rules are keyed by package identity (manager/source/id), never by a manager's own types.
/// </summary>
/// <remarks>
/// Only packages a manager can upgrade and that come from a source can have a rule (<see cref="CanAutoUpdate"/>):
/// local packages are offered by no source, so nothing would ever upgrade them. Rules of packages that are no
/// longer installed are dropped after a complete installed list (<see cref="PruneInstalled"/>) and right after an
/// uninstall (<see cref="Remove"/>). Used from the UI thread only; <see cref="Changed"/> is raised there.
/// </remarks>
public sealed partial class AutoUpdateService
{
    private readonly SettingsService _settings;
    private readonly ILogger<AutoUpdateService> _logger;

    public AutoUpdateService(SettingsService settings, ILogger<AutoUpdateService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Raised after the rules changed (turned on/off, forced, pruned).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<AutoUpdateRule> Rules => _settings.Current.AutoUpdates;

    /// <summary>The global switch: off pauses every rule.</summary>
    public bool IsGloballyEnabled => _settings.Current.AutoUpdatesEnabled;

    /// <summary>The global "update open apps too": every rule behaves as forced.</summary>
    public bool ForceOpenApps => _settings.Current.AutoUpdateOpenApps;

    /// <summary>A rule makes sense for this package: its manager can upgrade it and a source offers it.</summary>
    public static bool CanAutoUpdate(Package package, ManagerCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(package);
        return capabilities.HasFlag(ManagerCapabilities.Upgrade) && package.SourceId != PackageSource.LocalId;
    }

    public AutoUpdateRule? Find(Package package) => Rules.FirstOrDefault(r => r.IsFor(package));

    public bool IsEnabled(Package package) => Find(package) is not null;

    public bool IsForced(Package package) => Find(package)?.Force == true;

    /// <summary>
    /// True when the next check would act on this package's update (rule on and the global switch on): the "new
    /// updates" toast leaves it to the automatic-update notifications.
    /// </summary>
    public bool WillHandle(Package package) => IsGloballyEnabled && IsEnabled(package);

    /// <summary>Turns automatic updates of <paramref name="package"/> on (with <paramref name="force"/>) or off.</summary>
    /// <returns>False when saving failed (already logged).</returns>
    public bool Set(Package package, bool enabled, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        var current = Find(package);
        if (!enabled)
        {
            if (current is null)
            {
                return true;
            }

            LogTurnedOff(package.Key);
            return Save([.. Rules.Where(r => !r.IsFor(package))]);
        }

        if (current is not null && current.Force == force)
        {
            return true;
        }

        LogTurnedOn(package.Key, force);
        return Save([.. Rules.Where(r => !r.IsFor(package)), AutoUpdateRule.For(package, force)]);
    }

    /// <summary>The row button: on (keeping no force) when off, off when on.</summary>
    public bool Toggle(Package package) => Set(package, !IsEnabled(package));

    /// <summary>Drops the rule of a package that was uninstalled.</summary>
    public bool Remove(Package package) => Set(package, enabled: false);

    /// <summary>After a complete installed list: drops the rules of packages that are not installed any more.</summary>
    public void PruneInstalled(IReadOnlyList<Package> installed, bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(installed);
        if (!isComplete || Rules.Count == 0)
        {
            return;
        }

        var kept = Rules.Where(r => installed.Any(r.IsFor)).ToList();
        if (kept.Count != Rules.Count)
        {
            LogPruned(Rules.Count - kept.Count);
            Save(kept);
        }
    }

    private bool Save(IReadOnlyList<AutoUpdateRule> rules)
    {
        var saved = _settings.Update(s => s.AutoUpdates = rules);
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic updates on for {PackageKey} (even if open: {Force})")]
    private partial void LogTurnedOn(string packageKey, bool force);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic updates off for {PackageKey}")]
    private partial void LogTurnedOff(string packageKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped {Count} automatic-update rule(s) of packages no longer installed")]
    private partial void LogPruned(int count);
}
