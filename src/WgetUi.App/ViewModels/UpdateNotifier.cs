using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Platform;
using WgetUi.Core.Models;

namespace WgetUi.App.ViewModels;

/// <summary>
/// Toasts "N new updates" after a check, only for updates the user has not been told about or seen yet.
/// </summary>
/// <remarks>
/// An update is identified by package key + available version, so a newer version of the same package is
/// news again. The known set lives in settings (no repeat after a restart) and is replaced by the current
/// list after every complete check, which keeps it bounded. When the window is active the user sees the list
/// directly: new entries are recorded as seen without a toast. Nothing is shown until the user answered the
/// first-run question (<see cref="AppSettings.NotifyNewUpdates"/>).
/// </remarks>
public sealed partial class UpdateNotifier
{
    private const int NamesInBody = 3;
    private readonly SettingsService _settings;
    private readonly IToastNotifier _notifier;
    private readonly ILogger<UpdateNotifier> _logger;

    public UpdateNotifier(UpdatesViewModel updates, SettingsService settings, IToastNotifier notifier, ILogger<UpdateNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(updates);
        _settings = settings;
        _notifier = notifier;
        _logger = logger;
        updates.Loaded += (_, e) => OnLoaded(e.Packages, e.IsComplete);
    }

    /// <summary>Set by the shell: the main window is the active window.</summary>
    public bool IsWindowActive { get; set; }

    /// <summary>The first-run question has not been answered yet.</summary>
    public bool NeedsConsent => _settings.Current.NotifyNewUpdates is null;

    public static string KeyOf(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return $"{package.Key}@{package.AvailableVersion}";
    }

    public void OnLoaded(IReadOnlyList<Package> packages, bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var known = _settings.Current.NotifiedUpdates.ToHashSet(StringComparer.Ordinal);
        var fresh = packages.Where(p => !known.Contains(KeyOf(p))).ToList();
        var current = packages.Select(KeyOf).Distinct(StringComparer.Ordinal);

        // A manager that failed returned nothing: keep its entries instead of forgetting them.
        IReadOnlyList<string> next = isComplete ? [.. current] : [.. known.Union(current, StringComparer.Ordinal)];
        if (fresh.Count > 0 || next.Count != known.Count)
        {
            _settings.Update(s => s.NotifiedUpdates = next);
        }

        if (fresh.Count == 0 || IsWindowActive || _settings.Current.NotifyNewUpdates != true)
        {
            return;
        }

        var loc = Loc.Instance;
        var title = fresh.Count == 1 ? loc["Toast_NewUpdates_One"] : loc.Format("Toast_NewUpdates_Many", fresh.Count);
        var names = string.Join(", ", fresh.Take(NamesInBody).Select(p => p.Name));
        var body = fresh.Count > NamesInBody ? loc.Format("Toast_NewUpdates_More", names, fresh.Count - NamesInBody) : names;
        _notifier.Show(title, body);
        LogNotified(fresh.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notified {Count} new update(s)")]
    private partial void LogNotified(int count);
}
