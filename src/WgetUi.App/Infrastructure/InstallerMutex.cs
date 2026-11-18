using Microsoft.Extensions.Logging;

namespace WgetUi.App.Infrastructure;

/// <summary>
/// Fixed-name mutexes that tell the installer wgetui is running, so setup and uninstall ask the user to close
/// it instead of replacing files in use. Every process holds them for its whole lifetime, whatever its
/// <c>--data-dir</c>; the per-data-directory mutex of <see cref="SingleInstance"/> is a separate thing.
/// </summary>
/// <remarks>
/// Inno Setup's <c>AppMutex</c> only checks that a mutex with the name exists, so the handles are opened
/// without taking ownership. The session-local name covers the usual case; the <c>Global\</c> one lets a setup
/// started in another session see it too. Creating one can fail (e.g. another user's instance already created
/// the global one with its own permissions): that is logged and ignored, never fatal.
/// </remarks>
public sealed partial class InstallerMutex : IDisposable
{
    /// <summary>
    /// Used by <c>installer/wgetui.iss</c> (<c>AppMutex=wgetui-running,Global\wgetui-running</c>): never rename.
    /// </summary>
    public const string Name = "wgetui-running";

    public const string GlobalName = @"Global\" + Name;

    private readonly List<Mutex> _held;

    private InstallerMutex(List<Mutex> held, IReadOnlyList<(string Name, Exception Error)> failures)
    {
        _held = held;
        Failures = failures;
    }

    /// <summary>The mutexes that could not be created, and why (normally empty).</summary>
    public IReadOnlyList<(string Name, Exception Error)> Failures { get; }

    public static InstallerMutex Acquire()
    {
        var held = new List<Mutex>();
        var failures = new List<(string, Exception)>();
        foreach (var name in new[] { Name, GlobalName })
        {
            try
            {
                held.Add(new Mutex(initiallyOwned: false, name));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
            {
                failures.Add((name, ex));
            }
        }

        return new InstallerMutex(held, failures);
    }

    /// <summary>Logs the mutexes that could not be created, once the logger exists.</summary>
    public void LogFailures(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        foreach (var (name, error) in Failures)
        {
            LogMutexFailed(logger, name, error);
        }
    }

    public void Dispose()
    {
        foreach (var mutex in _held)
        {
            mutex.Dispose();
        }

        _held.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not create the installer mutex {Name}; the installer may not see this instance")]
    private static partial void LogMutexFailed(ILogger logger, string name, Exception ex);
}
