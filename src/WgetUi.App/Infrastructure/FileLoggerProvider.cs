using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WgetUi.App.Infrastructure;

/// <summary>
/// Minimal append-only file logger (one file, rolled to .old past 5 MB at startup). Enough for a desktop
/// app's diagnostics without another dependency.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private readonly Lock _gate = new();
    private readonly StreamWriter? _writer;

    public FileLoggerProvider(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new FileInfo(path);
            if (file.Exists && file.Length > MaxBytes)
            {
                File.Move(path, path + ".old", overwrite: true);
            }

            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The logger cannot log about itself: report on the debug channel and run without a file.
            Trace.TraceError($"wgetui: cannot open log file '{path}': {ex.Message}");
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (_writer is null)
        {
            return;
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category}: {message}");
        lock (_gate)
        {
            try
            {
                _writer.WriteLine(line);
                if (exception is not null)
                {
                    _writer.WriteLine(exception.ToString());
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Trace.TraceError($"wgetui: writing the log file failed: {ex.Message}");
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
