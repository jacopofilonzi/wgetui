using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services.AppUpdates;
using WgetUi.App.Services.Changelog;

namespace WgetUi.App.ViewModels;

/// <summary>
/// Decides when "What's new" opens: once, on the first start of a version newer than the last one that ran
/// (<see cref="AppSettings.LastRunVersion"/>, rules in <see cref="WhatsNewSelection"/>), and from Settings.
/// </summary>
/// <remarks>
/// <see cref="RecordStartup"/> runs at startup, before any window: it picks the notes and records the running
/// version right away, so they are shown once even if the window is never opened in this run.
/// <see cref="ShowPending"/> runs the first time the window is shown (a <c>--tray</c> start waits until the user
/// opens it). UI thread only.
/// </remarks>
public sealed partial class WhatsNewLauncher
{
    private readonly SettingsService _settings;
    private readonly IChangelogSource _changelog;
    private readonly Version _current;
    private readonly bool _justUpdated;
    private readonly WhatsNewOverlay _overlay;
    private readonly ILogger<WhatsNewLauncher> _logger;
    private IReadOnlyList<ChangelogVersion> _pending = [];

    public WhatsNewLauncher(SettingsService settings, IChangelogSource changelog, AppUpdateOptions options, AppPaths paths, WhatsNewOverlay overlay, ILogger<WhatsNewLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        _settings = settings;
        _changelog = changelog;
        _current = options.CurrentVersion;
        _justUpdated = paths.JustUpdated;
        _overlay = overlay;
        _logger = logger;
    }

    /// <summary>Notes waiting for the window to be shown.</summary>
    public bool HasPending => _pending.Count > 0;

    public void RecordStartup()
    {
        var current = _current.ToString(3);
        var last = _settings.Current.LastRunVersion;
        _pending = WhatsNewSelection.AtStartup(_changelog.Versions, last, _current, _justUpdated);
        if (!string.Equals(last, current, StringComparison.Ordinal))
        {
            _settings.Update(s => s.LastRunVersion = current);
        }

        if (_pending.Count > 0)
        {
            LogPending(last ?? "none", current, _pending.Count);
        }
    }

    /// <summary>Opens the popup with the notes picked at startup, once.</summary>
    public void ShowPending()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var versions = _pending;
        _pending = [];
        _overlay.Show(new WhatsNewViewModel(_current, versions));
    }

    /// <summary>Settings > About: every version up to the running one.</summary>
    public void ShowAll() => _overlay.Show(new WhatsNewViewModel(_current, WhatsNewSelection.UpTo(_changelog.Versions, _current)));

    [LoggerMessage(Level = LogLevel.Information, Message = "What's new after {Last} → {Current}: {Count} version(s) of notes")]
    private partial void LogPending(string last, string current, int count);
}
