using Microsoft.Extensions.Logging;
using WgetUi.App.Platform;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.App.Services;

/// <summary>
/// "Is this package's app open?" for automatic updates: the same rules as the relaunch after an upgrade
/// (<see cref="RelaunchPlanner"/>). The manager gives the install folder (read-only); the app is open when a
/// process of the user's session that the relaunch would restart runs from it (not elevated, not an
/// installer, not wgetui). Broad folders are refused.
/// </summary>
/// <remarks>
/// Unknown means "not open": no <see cref="ManagerCapabilities.InstallLocation"/>, no usable folder, or reading it
/// failed. The upgrade then goes ahead like a manual one would.
/// </remarks>
public sealed partial class OpenAppDetector
{
    private readonly ManagerRegistry _registry;
    private readonly IProcessSource _processes;
    private readonly RelaunchOptions _options;
    private readonly ILogger<OpenAppDetector> _logger;

    public OpenAppDetector(ManagerRegistry registry, IProcessSource processes, RelaunchOptions options, ILogger<OpenAppDetector> logger)
    {
        _registry = registry;
        _processes = processes;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IsOpenAsync(Package package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        var manager = _registry.Find(package.ManagerId);
        if (manager is null || !manager.Capabilities.HasFlag(ManagerCapabilities.InstallLocation))
        {
            return false;
        }

        string? folder;
        try
        {
            folder = await manager.GetInstallLocationAsync(package, cancellationToken);
        }
        catch (PackageManagerException ex)
        {
            LogLocationFailed(ex, package.Key, ex.ErrorCode);
            return false;
        }

        if (!RelaunchPlanner.IsUsableInstallFolder(folder, _options.BroadFolders))
        {
            return false;
        }

        var running = RelaunchPlanner.Select(_processes.ListSessionProcesses(), folder!, _options.OwnProcessId, _options.OwnSessionId, _options.OwnExecutable);
        if (running.Count > 0)
        {
            LogOpen(package.Key, running.Count, folder!);
        }

        return running.Count > 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{PackageKey} is open: {Count} process(es) running from {Folder}")]
    private partial void LogOpen(string packageKey, int count, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the install folder of {PackageKey} failed ({ErrorCode}): treated as not open")]
    private partial void LogLocationFailed(Exception ex, string packageKey, string errorCode);
}
