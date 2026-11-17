using System.Diagnostics;

namespace WgetUi.App.Platform.Windows;

/// <summary>
/// Process list through <see cref="Process"/>. A non-elevated process sees every process name (SYSTEM ones
/// included, e.g. consent.exe), though not the paths of elevated ones.
/// </summary>
public sealed class WindowsProcessSource : IProcessSource
{
    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
