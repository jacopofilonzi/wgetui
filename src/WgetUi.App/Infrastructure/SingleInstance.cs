using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WgetUi.App.Infrastructure;

/// <summary>
/// One running instance per data directory. The first launch owns a named mutex and listens on a named pipe;
/// a later launch with the same data directory sends its request (<see cref="InstanceMessage"/>) through the pipe and
/// exits. A different <c>--data-dir</c> gives a different key, so it is
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
    private const int MaxMessageLines = 8;
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
    public event EventHandler<InstanceMessage>? MessageReceived;

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
    public bool Send(InstanceMessage message, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect((int)timeout.TotalMilliseconds);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(message.Format());
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
                var lines = new List<string>();
                while (lines.Count < MaxMessageLines && await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false) is { } line)
                {
                    lines.Add(line);
                }

                if (InstanceMessage.Parse(lines) is { } message)
                {
                    MessageReceived?.Invoke(this, message);
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

/// <summary>What a later launch asks the running instance (see <see cref="SingleInstance"/> for the wire format).</summary>
/// <param name="Command"><see cref="SingleInstance.ActivateMessage"/> or <see cref="SingleInstance.TrayMessage"/>.</param>
/// <param name="ImportPath">A package list to open in the import popup (<c>--import</c>), or null.</param>
public sealed record InstanceMessage(string Command, string? ImportPath = null)
{
    private const string ImportField = "import:";

    /// <summary>The message for a launch with these arguments: an import always activates (it needs the window).</summary>
    public static InstanceMessage For(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new(paths.StartInTray ? SingleInstance.TrayMessage : SingleInstance.ActivateMessage, paths.ImportPath);
    }

    public string Format() =>
        ImportPath is null ? Command + "\n" : $"{Command}\n{ImportField}{ImportPath}\n";

    /// <summary>Reads the lines of a message; null when there is no command.</summary>
    public static InstanceMessage? Parse(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return null;
        }

        string? import = null;
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith(ImportField, StringComparison.Ordinal) && line.Length > ImportField.Length)
            {
                import = line[ImportField.Length..].Trim();
            }
        }

        return new InstanceMessage(lines[0].Trim(), string.IsNullOrEmpty(import) ? null : import);
    }
}
