namespace WgetUi.App.Infrastructure;

/// <summary>
/// Where the app keeps its files, plus the launch flags. Default: settings in %APPDATA%\wgetui, logs in
/// %LOCALAPPDATA%\wgetui\logs, setups downloaded by the self-update in %LOCALAPPDATA%\wgetui\updates.
/// <c>--data-dir &lt;path&gt;</c> puts all three under one folder (used for local checks
/// from an empty directory) and is also a separate single instance. <c>--tray</c> starts hidden in the tray
/// (used by "Start with Windows"). <c>--force-no-tray</c> (development and agent checks) disables the tray: no
/// icon, closing the window quits, <c>--tray</c> is ignored, so no instance is ever left running hidden.
/// <c>--updated</c> is passed by the setup when it reopens wgetui after a self-update (for "What's new"); it
/// changes nothing else. Unknown arguments are ignored.
/// </summary>
public sealed class AppPaths
{
    public const string DataDirArgument = "--data-dir";
    public const string TrayArgument = "--tray";
    public const string NoTrayArgument = "--force-no-tray";
    public const string UpdatedArgument = "--updated";

    private AppPaths(string dataDirectory, string localDirectory, string? explicitDataDirectory, bool startInTray, bool trayDisabled, bool justUpdated)
    {
        DataDirectory = dataDirectory;
        SettingsFile = Path.Combine(dataDirectory, "settings.json");
        LogFile = Path.Combine(localDirectory, "logs", "wgetui.log");
        UpdatesDirectory = Path.Combine(localDirectory, "updates");
        ExplicitDataDirectory = explicitDataDirectory;
        TrayDisabled = trayDisabled;
        StartInTray = startInTray && !trayDisabled;
        JustUpdated = justUpdated;
    }

    /// <summary>Folder of settings.json; also the single-instance key.</summary>
    public string DataDirectory { get; }

    public string SettingsFile { get; }

    public string LogFile { get; }

    /// <summary>Where the self-update downloads setups (emptied at startup by the installed copy).</summary>
    public string UpdatesDirectory { get; }

    /// <summary>The <c>--data-dir</c> value (full path), or null for the default folders.</summary>
    public string? ExplicitDataDirectory { get; }

    /// <summary>Started with <c>--tray</c>: no window until the user opens one.</summary>
    public bool StartInTray { get; }

    /// <summary>Started with <c>--force-no-tray</c>: no tray icon, closing the window quits.</summary>
    public bool TrayDisabled { get; }

    /// <summary>Started with <c>--updated</c>: the setup reopened wgetui after installing an update.</summary>
    public bool JustUpdated { get; }

    public static AppPaths FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var tray = args.Any(a => string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
        var noTray = args.Any(a => string.Equals(a, NoTrayArgument, StringComparison.OrdinalIgnoreCase));
        var updated = args.Any(a => string.Equals(a, UpdatedArgument, StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], DataDirArgument, StringComparison.OrdinalIgnoreCase))
            {
                var root = Path.GetFullPath(args[i + 1]);
                return new AppPaths(root, root, root, tray, noTray, updated);
            }
        }

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wgetui"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wgetui"),
            null,
            tray,
            noTray,
            updated);
    }
}
