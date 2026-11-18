using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Services;

/// <summary>
/// Restarts apps an upgrade closed. Some installers close the app's UI and restart only their service (e.g.
/// NetBird's tray app). Before an upgrade: ask the manager for the install folder and record the processes
/// running from it in the user's session (see <see cref="RelaunchPlanner"/>). After a successful upgrade and a
/// grace period (installers often restart their app themselves): start, non-elevated and with the original
/// arguments, each recorded program that is not running again.
/// </summary>
/// <remarks>
/// Gated by <see cref="AppSettings.RelaunchClosedApps"/>; managers without
/// <see cref="ManagerCapabilities.InstallLocation"/> are skipped. Starts processes for real.
/// </remarks>
public sealed partial class RelaunchAfterUpgrade : IOperationHook
{
    private readonly ManagerRegistry _registry;
    private readonly SettingsService _settings;
    private readonly IProcessSource _processes;
    private readonly IProcessLauncher _launcher;
    private readonly TimeProvider _time;
    private readonly TimeSpan _grace;
    private readonly IReadOnlyList<string> _broadFolders;
    private readonly int _ownProcessId;
    private readonly int _ownSessionId;
    private readonly string? _ownExecutable;
    private readonly ILogger<RelaunchAfterUpgrade> _logger;
    private readonly ConcurrentDictionary<long, IReadOnlyList<RelaunchTarget>> _snapshots = new();

    public RelaunchAfterUpgrade(ManagerRegistry registry, SettingsService settings, IProcessSource processes, IProcessLauncher launcher, TimeProvider time, RelaunchOptions options, ILogger<RelaunchAfterUpgrade> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _registry = registry;
        _settings = settings;
        _processes = processes;
        _launcher = launcher;
        _time = time;
        _grace = options.Grace;
        _broadFolders = options.BroadFolders;
        _ownProcessId = options.OwnProcessId;
        _ownSessionId = options.OwnSessionId;
        _ownExecutable = options.OwnExecutable;
        _logger = logger;
    }

    /// <summary>Completes when the relaunch started by the last <see cref="After"/> call is done (for tests).</summary>
    public Task LastRelaunch { get; private set; } = Task.CompletedTask;

    public async Task BeforeAsync(QueuedOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        // Upgrades and installs: installing a specific version over an installed package (e.g. a downgrade) closes
        // its apps just like an upgrade. A fresh install has no install location yet, so it records nothing.
        if (operation.Kind is not (OperationKind.Upgrade or OperationKind.Install) || !_settings.Current.RelaunchClosedApps)
        {
            return;
        }

        var manager = _registry.Find(operation.Package.ManagerId);
        if (manager is null || !manager.Capabilities.HasFlag(ManagerCapabilities.InstallLocation))
        {
            return;
        }

        string? folder;
        try
        {
            folder = await manager.GetInstallLocationAsync(operation.Package, cancellationToken).ConfigureAwait(false);
        }
        catch (PackageManagerException ex)
        {
            LogLocationFailed(ex, operation.Package.Key, ex.ErrorCode);
            return;
        }

        if (!RelaunchPlanner.IsUsableInstallFolder(folder, _broadFolders))
        {
            LogNoFolder(operation.Package.Key, folder);
            return;
        }

        var targets = RelaunchPlanner.Select(_processes.ListSessionProcesses(), folder!, _ownProcessId, _ownSessionId, _ownExecutable);
        if (targets.Count > 0)
        {
            _snapshots[operation.Sequence] = targets;
            LogSnapshot(operation.Package.Key, targets.Count, folder!);
        }
    }

    public void After(QueuedOperation operation, OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(result);
        if (!_snapshots.TryRemove(operation.Sequence, out var targets) || !result.Succeeded)
        {
            return;
        }

        LastRelaunch = RelaunchAsync(operation.Package, targets);
    }

    private async Task RelaunchAsync(Package package, IReadOnlyList<RelaunchTarget> targets)
    {
        await Task.Delay(_grace, _time).ConfigureAwait(false);
        foreach (var target in RelaunchPlanner.Missing(targets, _processes.ListSessionProcesses()))
        {
            if (!File.Exists(target.ExecutablePath))
            {
                // Versioned install folders move on upgrade: the old executable is gone.
                LogGone(package.Key, target.ExecutablePath);
                continue;
            }

            try
            {
                _launcher.Start(target.ExecutablePath, target.Arguments, target.WorkingDirectory);
                LogRelaunched(package.Key, target.ExecutablePath);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                LogRelaunchFailed(ex, package.Key, target.ExecutablePath);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Upgrade of {PackageKey}: {Count} process(es) running from {Folder} recorded for relaunch")]
    private partial void LogSnapshot(string packageKey, int count, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "Upgrade of {PackageKey}: no usable install folder ({Folder}), nothing to relaunch")]
    private partial void LogNoFolder(string packageKey, string? folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upgrade of {PackageKey}: reading the install folder failed ({ErrorCode})")]
    private partial void LogLocationFailed(Exception ex, string packageKey, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Relaunched {Executable} after upgrading {PackageKey}")]
    private partial void LogRelaunched(string packageKey, string executable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Not relaunching {Executable} after upgrading {PackageKey}: the file is gone")]
    private partial void LogGone(string packageKey, string executable);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relaunching {Executable} after upgrading {PackageKey} failed")]
    private partial void LogRelaunchFailed(Exception ex, string packageKey, string executable);
}

/// <summary>Fixed inputs of <see cref="RelaunchAfterUpgrade"/>; <see cref="ForCurrentProcess"/> in the app, explicit in tests.</summary>
/// <param name="Grace">Wait after the upgrade before checking what came back.</param>
/// <param name="BroadFolders">Folders never accepted as an install folder.</param>
/// <param name="OwnProcessId">This app's process id.</param>
/// <param name="OwnSessionId">This app's Windows session.</param>
/// <param name="OwnExecutable">This app's executable.</param>
public sealed record RelaunchOptions(TimeSpan Grace, IReadOnlyList<string> BroadFolders, int OwnProcessId, int OwnSessionId, string? OwnExecutable)
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(5);

    public static RelaunchOptions ForCurrentProcess()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        Environment.SpecialFolder[] broad =
        [
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.Desktop,
        ];
        var folders = broad.Select(Environment.GetFolderPath).Where(f => f.Length > 0).ToList();
        var localApps = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        folders.Add(Path.Combine(localApps, "Programs"));
        folders.Add(Path.Combine(localApps, "Microsoft", "WindowsApps"));
        return new RelaunchOptions(DefaultGrace, folders, Environment.ProcessId, self.SessionId, Environment.ProcessPath);
    }
}
