using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.Core.Models;
using WgetUi.Core.Updates;

namespace WgetUi.App.Services;

/// <summary>
/// The "ignore this version" records, persisted in settings.json (<see cref="AppSettings.IgnoredUpdates"/>).
/// The rules (what is hidden, when a record is dropped) are Core's <see cref="IgnoredUpdateFilter"/>.
/// </summary>
/// <remarks>Used from the UI thread only; <see cref="Changed"/> is raised there.</remarks>
public sealed partial class IgnoredUpdatesService
{
    private readonly SettingsService _settings;
    private readonly ILogger<IgnoredUpdatesService> _logger;

    public IgnoredUpdatesService(SettingsService settings, ILogger<IgnoredUpdatesService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Raised after the records changed (ignored, restored or pruned).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<IgnoredUpdate> Records => _settings.Current.IgnoredUpdates;

    /// <summary>True when the update <paramref name="package"/> offers is ignored.</summary>
    public bool IsIgnored(Package package) => Records.Any(r => r.Matches(package));

    /// <summary>Ignores the version <paramref name="package"/> offers. False when it offers none or saving failed.</summary>
    public bool Ignore(Package package)
    {
        if (IgnoredUpdate.For(package) is not { } record || IsIgnored(package))
        {
            return false;
        }

        // Ignoring a newer version replaces the record of the older one.
        var saved = Save([.. Records.Where(r => !r.IsFor(package)), record]);
        LogIgnored(record.PackageId, record.Version);
        return saved;
    }

    /// <summary>Stops ignoring that update.</summary>
    public bool Restore(IgnoredUpdate record)
    {
        ArgumentNullException.ThrowIfNull(record);
        LogRestored(record.PackageId, record.Version);
        return Save([.. Records.Where(r => r != record)]);
    }

    /// <summary>Applies the records to a fresh list of updates and saves the pruned records.</summary>
    public IgnoredUpdateResult Apply(IReadOnlyList<Package> updates, bool isComplete)
    {
        var result = IgnoredUpdateFilter.Apply(updates, Records, isComplete);
        if (result.Changed)
        {
            LogPruned(Records.Count - result.Records.Count);
            Save(result.Records);
        }

        return result;
    }

    private bool Save(IReadOnlyList<IgnoredUpdate> records)
    {
        var saved = _settings.Update(s => s.IgnoredUpdates = records);
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ignoring {PackageId} {Version}")]
    private partial void LogIgnored(string packageId, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "No longer ignoring {PackageId} {Version}")]
    private partial void LogRestored(string packageId, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped {Count} stale ignored update(s)")]
    private partial void LogPruned(int count);
}
