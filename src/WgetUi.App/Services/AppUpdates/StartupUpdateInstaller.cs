using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;

namespace WgetUi.App.Services.AppUpdates;

/// <summary>
/// Installs a downloaded update by itself at the next start, before any UI: the end of the automatic self-update
/// (check → background download → "Restart to update", or this at the next start, Start with Windows included).
/// </summary>
/// <remarks>
/// Called by Program.Main once this process is the primary instance (a second launch only hands over, so it
/// never triggers it). It starts the setup only when all hold: this is the installed copy, "update wgetui
/// automatically" is on (<see cref="AppSettings.CheckAppUpdates"/>), <see cref="AppSettings.PendingAppUpdate"/>
/// names a version newer than the running one whose file in the updates folder still matches its SHA-256, and
/// that version was not attempted already (<see cref="AppSettings.AppUpdateAutoInstallAttempted"/>, recorded
/// before the start). When wgetui starts again still older than an attempted version, the install failed: it is
/// left to the sidebar item ("Retry update") instead of looping. Once the running version reaches the pending
/// one both records are cleared. The setup gets <see cref="SetupCommand"/> arguments (<c>/TRAY</c> for a
/// <c>--tray</c> start) and waits for this process to exit; the caller exits right away. Operations can't be
/// queued this early, so nothing is asked. Starting the setup acts for real: tests use fakes.
/// </remarks>
public sealed partial class StartupUpdateInstaller
{
    private readonly SettingsService _settings;
    private readonly AppUpdateOptions _options;
    private readonly IAppInstallInfo _installInfo;
    private readonly IAppUpdateDownloader _downloader;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<StartupUpdateInstaller> _logger;

    public StartupUpdateInstaller(SettingsService settings, AppUpdateOptions options, IAppInstallInfo installInfo, IAppUpdateDownloader downloader, IProcessLauncher launcher, ILogger<StartupUpdateInstaller> logger)
    {
        _settings = settings;
        _options = options;
        _installInfo = installInfo;
        _downloader = downloader;
        _launcher = launcher;
        _logger = logger;
    }

    /// <summary>The pending record as a release (null when missing or malformed: a bad version, a file name that is not the setup's, a bad hash).</summary>
    public static AppRelease? ReadPending(PendingAppUpdate? pending)
    {
        if (pending is null || AppVersion.TryParse(pending.Version) is not { } version)
        {
            return null;
        }

        var text = version.ToString(3);
        var asset = GitHubReleaseParser.SetupAssetName(text);
        if (!string.Equals(pending.FileName, asset, StringComparison.Ordinal) || !Sha256Hex().IsMatch(pending.Sha256))
        {
            return null;
        }

        var tag = "v" + text;
        var url = new Uri($"https://github.com/jacopofilonzi/wgetui/releases/download/{tag}/{asset}");
        return new AppRelease(version, tag, asset, url, 0, pending.Sha256);
    }

    /// <summary>The record saved after a verified download.</summary>
    public static PendingAppUpdate ToRecord(AppRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new PendingAppUpdate { Version = release.VersionText, FileName = release.AssetName, Sha256 = release.Sha256 };
    }

    /// <summary>Starts the pending setup if every rule allows it; true means the caller must exit now.</summary>
    public bool TryStart(bool startInTray)
    {
        if (!_options.IsInstalledCopy(_installInfo.InstallLocation) || _settings.Current.PendingAppUpdate is null)
        {
            return false;
        }

        var release = ReadPending(_settings.Current.PendingAppUpdate);
        if (release is null || release.Version <= _options.CurrentVersion)
        {
            // Installed (or a record that can't be used): nothing left to do.
            LogCleared(_settings.Current.PendingAppUpdate.Version, _options.CurrentVersion);
            Clear();
            return false;
        }

        if (!_settings.Current.CheckAppUpdates)
        {
            LogAutomaticOff(release.VersionText);
            return false;
        }

        if (string.Equals(_settings.Current.AppUpdateAutoInstallAttempted, release.VersionText, StringComparison.Ordinal))
        {
            LogAlreadyAttempted(release.VersionText);
            return false;
        }

        var path = Path.Combine(_options.DownloadDirectory, release.AssetName);
        if (!_downloader.Verify(path, release))
        {
            LogNotVerified(path);
            Clear();
            return false;
        }

        _settings.Update(s => s.AppUpdateAutoInstallAttempted = release.VersionText);
        try
        {
            _launcher.Start(path, SetupCommand.Arguments(_options.DownloadDirectory, startInTray), _options.DownloadDirectory);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            LogLaunchFailed(release.VersionText, ex);
            return false;
        }

        LogStarted(release.VersionText, startInTray);
        return true;
    }

    private void Clear() => _settings.Update(s =>
    {
        s.PendingAppUpdate = null;
        s.AppUpdateAutoInstallAttempted = null;
    });

    [GeneratedRegex(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Hex();

    [LoggerMessage(Level = LogLevel.Information, Message = "Pending wgetui update {Pending} cleared (running {Current})")]
    private partial void LogCleared(string pending, Version current);

    [LoggerMessage(Level = LogLevel.Information, Message = "wgetui {Version} is downloaded but automatic updates are off: not installed at startup")]
    private partial void LogAutomaticOff(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The wgetui {Version} setup was already started and wgetui is still older: not starting it again (Retry update in the menu)")]
    private partial void LogAlreadyAttempted(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pending setup {Path} is missing or no longer matches its SHA-256: dropped")]
    private partial void LogNotVerified(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start the wgetui {Version} setup at startup")]
    private partial void LogLaunchFailed(string version, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the wgetui {Version} setup at startup (tray: {Tray}); exiting")]
    private partial void LogStarted(string version, bool tray);
}
