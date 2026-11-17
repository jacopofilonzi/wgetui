using System.Diagnostics;

namespace WgetUi.App.Platform.Windows;

/// <summary>
/// Starts a program with this app's token. wgetui always runs non-elevated (asInvoker manifest), so the
/// program does too. Acts for real.
/// </summary>
public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public void Start(string executablePath, string arguments, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(executablePath, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        });
    }
}
