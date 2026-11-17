namespace WgetUi.App.Platform;

/// <summary>Read-only view of running processes. Behind an interface so tests script what is running.</summary>
public interface IProcessSource
{
    /// <summary>True when a process with this image name (without ".exe") runs, in any session.</summary>
    bool IsRunning(string processName);
}
