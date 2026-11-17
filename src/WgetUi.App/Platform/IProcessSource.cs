namespace WgetUi.App.Platform;

/// <summary>
/// A running process as far as a non-elevated app can read it. Unreadable fields are null (protected processes,
/// other users); elevated ones are flagged by IsElevated.
/// </summary>
/// <param name="Id">Process id.</param>
/// <param name="ParentId">Id of the process that started it (may have exited or been reused), or null when unreadable.</param>
/// <param name="SessionId">Windows session (0 = services).</param>
/// <param name="ExecutablePath">Full path of the image, or null when unreadable.</param>
/// <param name="CommandLine">Full command line (executable included), or null when unreadable.</param>
/// <param name="IsElevated">Runs elevated; null when the token is unreadable.</param>
public sealed record ProcessSnapshot(int Id, int? ParentId, int SessionId, string? ExecutablePath, string? CommandLine, bool? IsElevated);

/// <summary>Read-only view of running processes. Behind an interface so tests script what is running.</summary>
public interface IProcessSource
{
    /// <summary>True when a process with this image name (without ".exe") runs, in any session.</summary>
    bool IsRunning(string processName);

    /// <summary>Processes of the current Windows session, with what could be read of each.</summary>
    IReadOnlyList<ProcessSnapshot> ListSessionProcesses();
}

/// <summary>Starts a program as the current (non-elevated) user. Acts for real.</summary>
public interface IProcessLauncher
{
    void Start(string executablePath, string arguments, string workingDirectory);
}
