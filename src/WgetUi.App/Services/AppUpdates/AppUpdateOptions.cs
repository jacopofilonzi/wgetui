using System.Net.Http.Headers;
using WgetUi.App.Infrastructure;

namespace WgetUi.App.Services.AppUpdates;

/// <summary>
/// What the self-update needs to know about this process: its version, the folder its exe runs from (to tell
/// the installed copy from a dev or portable one) and where downloads go.
/// </summary>
/// <param name="CurrentVersion">Running version (x.y.z).</param>
/// <param name="ExecutableDirectory">Folder of the running exe.</param>
/// <param name="DownloadDirectory">Folder of downloaded setups (<see cref="AppPaths.UpdatesDirectory"/>).</param>
public sealed record AppUpdateOptions(Version CurrentVersion, string ExecutableDirectory, string DownloadDirectory)
{
    /// <summary><c>wgetui/x.y.z</c>: the only identifying header sent to GitHub.</summary>
    public ProductInfoHeaderValue UserAgent { get; } = new("wgetui", CurrentVersion.ToString(3));

    public static AppUpdateOptions ForCurrentProcess(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var exe = Environment.ProcessPath;
        var directory = exe is null ? AppContext.BaseDirectory : Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        return new AppUpdateOptions(AppVersion.Value, directory, paths.UpdatesDirectory);
    }

    /// <summary>
    /// True when the exe runs from the folder the installer recorded (normalized, case-insensitive): only that
    /// copy updates itself. A dev build, a portable copy or a missing install record never does.
    /// </summary>
    public bool IsInstalledCopy(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return false;
        }

        var installed = Normalize(installLocation);
        var running = Normalize(ExecutableDirectory);
        return installed is not null && running is not null && string.Equals(installed, running, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
