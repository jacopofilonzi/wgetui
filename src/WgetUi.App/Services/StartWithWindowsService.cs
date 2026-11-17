using System.Security;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;

namespace WgetUi.App.Services;

/// <summary>
/// "Start with Windows": a Run entry that launches this exe with <c>--tray</c> (and the same
/// <c>--data-dir</c>, if any). The Run key is the source of truth, so removing the entry elsewhere (Task
/// Manager → Startup apps) shows as off. Off by default: nothing is written until the user turns it on.
/// </summary>
public sealed partial class StartWithWindowsService
{
    public const string ValueName = "wgetui";
    private readonly IRunKey _runKey;
    private readonly AppPaths _paths;
    private readonly string? _executable;
    private readonly ILogger<StartWithWindowsService> _logger;

    public StartWithWindowsService(IRunKey runKey, AppPaths paths, ILogger<StartWithWindowsService> logger)
        : this(runKey, paths, Environment.ProcessPath, logger)
    {
    }

    public StartWithWindowsService(IRunKey runKey, AppPaths paths, string? executable, ILogger<StartWithWindowsService> logger)
    {
        _runKey = runKey;
        _paths = paths;
        _executable = executable;
        _logger = logger;
    }

    /// <summary>The command written to the Run key.</summary>
    public string Command
    {
        get
        {
            var command = $"\"{_executable}\" {AppPaths.TrayArgument}";
            return _paths.ExplicitDataDirectory is { } dir ? $"{command} {AppPaths.DataDirArgument} \"{dir}\"" : command;
        }
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(_runKey.Read(ValueName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                LogReadFailed(ex);
                return false;
            }
        }
    }

    /// <summary>Adds or removes the Run entry. Returns false when that failed (logged).</summary>
    public bool SetEnabled(bool enabled)
    {
        if (enabled && string.IsNullOrEmpty(_executable))
        {
            LogNoExecutable();
            return false;
        }

        try
        {
            if (enabled)
            {
                _runKey.Write(ValueName, Command);
            }
            else
            {
                _runKey.Delete(ValueName);
            }

            LogChanged(enabled);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            LogWriteFailed(ex, enabled);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Start with Windows set to {Enabled}")]
    private partial void LogChanged(bool enabled);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the Run key")]
    private partial void LogReadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not set Start with Windows to {Enabled}")]
    private partial void LogWriteFailed(Exception ex, bool enabled);

    [LoggerMessage(Level = LogLevel.Error, Message = "Start with Windows: the executable path is unknown")]
    private partial void LogNoExecutable();
}
