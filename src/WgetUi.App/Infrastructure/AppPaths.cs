namespace WgetUi.App.Infrastructure;

/// <summary>
/// Where the app keeps its files. Default: settings in %APPDATA%\wgetui, logs in %LOCALAPPDATA%\wgetui\logs.
/// <c>--data-dir &lt;path&gt;</c> puts both under one folder (used for local checks from an empty directory).
/// </summary>
public sealed class AppPaths
{
    private AppPaths(string settingsDirectory, string logDirectory)
    {
        SettingsFile = Path.Combine(settingsDirectory, "settings.json");
        LogFile = Path.Combine(logDirectory, "wgetui.log");
    }

    public string SettingsFile { get; }

    public string LogFile { get; }

    public static AppPaths FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                var root = Path.GetFullPath(args[i + 1]);
                return new AppPaths(root, Path.Combine(root, "logs"));
            }
        }

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wgetui"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wgetui", "logs"));
    }
}
