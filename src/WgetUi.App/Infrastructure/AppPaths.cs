namespace WgetUi.App.Infrastructure;

/// <summary>
/// Where the app keeps its files, plus the launch flags. Default: settings in %APPDATA%\wgetui, logs in
/// %LOCALAPPDATA%\wgetui\logs. <c>--data-dir &lt;path&gt;</c> puts both under one folder (used for local checks
/// from an empty directory) and is also a separate single instance. <c>--tray</c> starts hidden in the tray
/// (used by "Start with Windows"). <c>--force-no-tray</c> (development and agent checks) disables the tray: no
/// icon, closing the window quits, <c>--tray</c> is ignored, so no instance is ever left running hidden.
/// </summary>
public sealed class AppPaths
{
    public const string DataDirArgument = "--data-dir";
    public const string TrayArgument = "--tray";
    public const string NoTrayArgument = "--force-no-tray";

    private AppPaths(string dataDirectory, string logDirectory, string? explicitDataDirectory, bool startInTray, bool trayDisabled)
    {
        DataDirectory = dataDirectory;
        SettingsFile = Path.Combine(dataDirectory, "settings.json");
        LogFile = Path.Combine(logDirectory, "wgetui.log");
        ExplicitDataDirectory = explicitDataDirectory;
        TrayDisabled = trayDisabled;
        StartInTray = startInTray && !trayDisabled;
    }

    /// <summary>Folder of settings.json; also the single-instance key.</summary>
    public string DataDirectory { get; }

    public string SettingsFile { get; }

    public string LogFile { get; }

    /// <summary>The <c>--data-dir</c> value (full path), or null for the default folders.</summary>
    public string? ExplicitDataDirectory { get; }

    /// <summary>Started with <c>--tray</c>: no window until the user opens one.</summary>
    public bool StartInTray { get; }

    /// <summary>Started with <c>--force-no-tray</c>: no tray icon, closing the window quits.</summary>
    public bool TrayDisabled { get; }

    public static AppPaths FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var tray = args.Any(a => string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
        var noTray = args.Any(a => string.Equals(a, NoTrayArgument, StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], DataDirArgument, StringComparison.OrdinalIgnoreCase))
            {
                var root = Path.GetFullPath(args[i + 1]);
                return new AppPaths(root, Path.Combine(root, "logs"), root, tray, noTray);
            }
        }

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wgetui"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wgetui", "logs"),
            null,
            tray,
            noTray);
    }
}
