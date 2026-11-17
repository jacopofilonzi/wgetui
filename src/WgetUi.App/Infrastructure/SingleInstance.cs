using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WgetUi.App.Infrastructure;

/// <summary>
/// One running instance per data directory. The first launch owns a named mutex and listens on a named pipe;
/// a later launch with the same data directory sends its request ("activate", or "tray" when started with
/// <c>--tray</c>) through the pipe and exits. A different <c>--data-dir</c> gives a different key, so it is
/// a separate instance (local checks never collide with the user's own copy).
/// </summary>
/// <remarks>
/// The key hashes the user name and the full data-directory path (case-insensitive): pipe names are
/// machine-wide, the mutex is per session (<c>Local\</c>).
/// </remarks>
public sealed partial class SingleInstance : IDisposable
{
    public const string ActivateMessage = "activate";
    public const string TrayMessage = "tray";
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private ILogger? _logger;
    private Task? _listener;

    private SingleInstance(Mutex mutex, bool isPrimary, string pipeName)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        _pipeName = pipeName;
    }

    /// <summary>Raised on a background thread with the message of a later launch.</summary>
    public event EventHandler<string>? MessageReceived;

    public bool IsPrimary { get; }

    public static string KeyFor(string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}|{Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar)}".ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }

    public static SingleInstance Acquire(string key)
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\wgetui-{key}", out var createdNew);
        return new SingleInstance(mutex, createdNew, $"wgetui-{key}");
    }

    /// <summary>Primary only: starts listening for later launches.</summary>
    public void Listen(ILogger logger)
    {
        if (!IsPrimary || _listener is not null)
        {
            return;
        }

        _logger = logger;
        _listener = Task.Run(ListenAsync);
    }

    /// <summary>Secondary only: hands the request to the running instance. Returns false if it could not be reached.</summary>
    public bool Send(string message, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect((int)timeout.TotalMilliseconds);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.WriteLine(message);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Already logged by the listener.
        }

        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Released from another thread than the owner (disposed off the UI thread): the OS frees it at exit.
            }
        }

        _mutex.Dispose();
        _stop.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    MessageReceived?.Invoke(this, message.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                if (_logger is not null)
                {
                    LogPipeFailed(_logger, ex);
                }

                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Single-instance pipe failed; listening again")]
    private static partial void LogPipeFailed(ILogger logger, Exception ex);
}
