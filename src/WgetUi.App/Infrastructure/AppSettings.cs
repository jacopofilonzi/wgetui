using System.Text.Json.Serialization;
using WgetUi.Core.Models;

namespace WgetUi.App.Infrastructure;

/// <summary>User settings, persisted as JSON. Unknown values fall back to defaults when applied.</summary>
public sealed class AppSettings
{
    public const string System = "system";

    /// <summary>"system", "en" or "it".</summary>
    public string Language { get; set; } = System;

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = System;

    /// <summary>Accept package and source agreements without showing them. Off by default.</summary>
    public bool AutoAcceptAgreements { get; set; }

    /// <summary>Closing the window hides it to the notification area instead of quitting. On by default.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>The one-time "wgetui keeps running in the tray" notification was shown.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Toast when new updates are found. Null until the user answered the first-run question.</summary>
    public bool? NotifyNewUpdates { get; set; }

    /// <summary>
    /// Updates already notified or seen, as "&lt;package key&gt;@&lt;available version&gt;". Replaced by the current list after
    /// each complete check, so it never grows past the number of pending updates.
    /// </summary>
    public IReadOnlyList<string> NotifiedUpdates { get; set; } = [];

    /// <summary>Restart the apps an upgrade closed (processes running from the package folder). On by default.</summary>
    public bool RelaunchClosedApps { get; set; } = true;

    /// <summary>
    /// Updates the user ignored (one exact version each). Pruned after each check by
    /// <see cref="Core.Updates.IgnoredUpdateFilter"/>, so it never grows past the number of pending updates.
    /// </summary>
    public IReadOnlyList<IgnoredUpdate> IgnoredUpdates { get; set; } = [];

    /// <summary>
    /// Packages upgraded automatically at the update checks (one rule per package, with its "even if open" flag).
    /// Pruned after each complete installed list, so it never grows past the number of installed packages.
    /// </summary>
    public IReadOnlyList<AutoUpdateRule> AutoUpdates { get; set; } = [];

    /// <summary>Global switch for <see cref="AutoUpdates"/>: off pauses them all without dropping the rules. On by default.</summary>
    public bool AutoUpdatesEnabled { get; set; } = true;

    /// <summary>Upgrade automatically even the apps that are open, as if every rule had Force. Off by default.</summary>
    public bool AutoUpdateOpenApps { get; set; }

    /// <summary>
    /// Automatic updates already notified as postponed (app open), as "&lt;package key&gt;@&lt;available version&gt;".
    /// Kept only while that update is still pending, so it never grows past the number of pending updates.
    /// </summary>
    public IReadOnlyList<string> PostponedUpdates { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
