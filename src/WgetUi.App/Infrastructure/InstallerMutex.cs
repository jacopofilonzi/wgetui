using Microsoft.Extensions.Logging;

namespace WgetUi.App.Infrastructure;

/// <summary>
/// A fixed-name mutex that tells the installer wgetui is running, so setup and uninstall ask the user to close
/// it instead of replacing files in use. Every process holds it for its whole lifetime, whatever its
/// <c>--data-dir</c>; the per-data-directory mutex of <see cref="SingleInstance"/> is a separate thing.
/// </summary>
/// <remarks>
/// Inno Setup's <c>AppMutex</c> only checks that a mutex with the name exists, so the handle is opened without
/// taking ownership. A session-local name is enough: the install is per-user, so the setup runs in the same
/// session as the instances it must see. Creating it can fail: that is logged and ignored, never fatal.
/// </remarks>
public sealed partial class InstallerMutex : IDisposable
{
    /// <summary>Used by <c>installer/wgetui.iss</c> (<c>AppMutex=wgetui-running</c>): never rename.</summary>
    public const string Name = "wgetui-running";

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
        foreach (var name in new[] { Name })
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
