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
    /// Pruned after each installed list (managers that failed keep their rules), so it never grows past the number of installed packages.
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

    /// <summary>
    /// Keep wgetui up to date by itself (installed copy only): check at startup and every hour, download a newer
    /// version in the background, install it at the next start. On by default.
    /// </summary>
    public bool CheckAppUpdates { get; set; } = true;

    /// <summary>The wgetui version ("x.y.z") last announced as ready by a notification, so each is notified once.</summary>
    public string? AppUpdateNotifiedVersion { get; set; }

    /// <summary>
    /// The wgetui version ("x.y.z") that ran last, so "What's new" shows the notes of the versions in between once.
    /// Null until the first start of a version that records it.
    /// </summary>
    public string? LastRunVersion { get; set; }

    /// <summary>
    /// A downloaded and verified wgetui setup not installed yet (in the updates folder): installed by itself at the
    /// next start. Cleared once the running version reaches it, or when the file no longer matches.
    /// </summary>
    public PendingAppUpdate? PendingAppUpdate { get; set; }

    /// <summary>
    /// The version ("x.y.z") whose setup was last started (at startup or by "Restart to update"). If wgetui starts
    /// again still older than it, the install failed: it is not started by itself again (loop protection).
    /// Cleared with <see cref="PendingAppUpdate"/>.
    /// </summary>
    public string? AppUpdateAutoInstallAttempted { get; set; }
}

/// <summary>A downloaded, verified wgetui setup waiting to be installed.</summary>
public sealed class PendingAppUpdate
{
    /// <summary>"x.y.z".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>File name in the updates folder (<c>wgetui-setup-x.y.z-x64.exe</c>, never a path).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>SHA-256 the file must still match before it is started, 64 lowercase hex characters.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
