using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;
using WgetUi.App.Services;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

public sealed class RelaunchTests : IAsyncDisposable
{
    private const int Session = 2;
    private const int Self = 9000;
    private static readonly string AppFolder = Path.Combine(Path.GetTempPath(), "wgetui-tests-apps", "NetBird");

    private readonly ScriptedManager _manager = new() { ExtraCapabilities = ManagerCapabilities.InstallLocation };
    private readonly FakeProcessSource _processes = new();
    private readonly FakeLauncher _launcher = new();
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "wgetui-tests", Guid.NewGuid().ToString("N"));
    private readonly RelaunchAfterUpgrade _hook;
    private readonly OperationQueue _queue;
    private readonly string _ui;
    private readonly string _service;

    public RelaunchTests()
    {
        (_settings, _dataDir) = TestSettings.Create();
        var folder = Path.Combine(_workDir, "NetBird");
        Directory.CreateDirectory(folder);
        _ui = Path.Combine(folder, "netbird-ui.exe");
        _service = Path.Combine(folder, "netbird.exe");
        File.WriteAllText(_ui, string.Empty);
        File.WriteAllText(_service, string.Empty);
        _manager.InstallLocations["netbird"] = folder;

        var registry = new ManagerRegistry([_manager], NullLogger<ManagerRegistry>.Instance);
        var options = new RelaunchOptions(TimeSpan.Zero, [Path.GetTempPath()], Self, Session, @"C:\Tools\wgetui.exe");
        _hook = new RelaunchAfterUpgrade(registry, _settings, _processes, _launcher, TimeProvider.System, options, NullLogger<RelaunchAfterUpgrade>.Instance);
        _queue = new OperationQueue(registry, [_hook], NullLogger<OperationQueue>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
        foreach (var dir in new[] { _dataDir, _workDir })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static ProcessSnapshot Proc(int id, string? path, string? commandLine = null, bool? elevated = false, int session = Session, int? parent = 1) =>
        new(id, parent, session, path, commandLine ?? (path is null ? null : $"\"{path}\""), elevated);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<OperationResult> UpgradeAsync(Action whileRunning, OperationResult outcome)
    {
        var operation = _queue.Enqueue(new Package("fake", "main", "netbird", "NetBird", "1.0", "2.0"), OperationKind.Upgrade);
        for (var i = 0; i < 200 && operation.State != OperationState.Running; i++)
        {
            await Task.Delay(10, Ct);
        }

        await Task.Delay(50, Ct); // Let the Before hook take its snapshot.
        whileRunning();
        _manager.Complete("netbird", outcome);
        var result = await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        // Completion is signalled just before the queue runs the After hooks: give them a moment.
        await Task.Delay(200, Ct);
        await _hook.LastRelaunch.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        return result;
    }

    [Fact]
    public async Task AppClosedByTheUpgradeIsStartedAgainWithItsArguments()
    {
        _processes.Processes.AddRange([Proc(10, _ui, $"\"{_ui}\" --minimized"), Proc(11, _service, $"{_service} service run")]);

        // The installer stops both; only the service comes back by itself.
        await UpgradeAsync(() => _processes.Processes.RemoveAll(p => p.Id == 10), OperationResult.Success());

        var started = Assert.Single(_launcher.Started);
        Assert.Equal(_ui, started.Executable);
        Assert.Equal("--minimized", started.Arguments);
        Assert.Equal(Path.GetDirectoryName(_ui), started.WorkingDirectory);
    }

    [Fact]
    public async Task NothingIsStartedWhenTheUpgradeFailsOrTheAppIsBack()
    {
        _processes.Processes.Add(Proc(10, _ui));
        await UpgradeAsync(() => _processes.Processes.Clear(), OperationResult.Failure(ErrorCodes.InstallFailed, "1603"));

        _processes.Processes.Add(Proc(10, _ui));
        await UpgradeAsync(() => _processes.Processes[0] = Proc(20, _ui), OperationResult.Success());

        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task SettingOffOrManagerWithoutInstallLocationSkipsEverything()
    {
        _settings.Update(s => s.RelaunchClosedApps = false);
        _processes.Processes.Add(Proc(10, _ui));
        await UpgradeAsync(() => _processes.Processes.Clear(), OperationResult.Success());

        _settings.Update(s => s.RelaunchClosedApps = true);
        _manager.ExtraCapabilities = ManagerCapabilities.None;
        _processes.Processes.Add(Proc(10, _ui));
        await UpgradeAsync(() => _processes.Processes.Clear(), OperationResult.Success());

        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public void SelectionSkipsWhatMustNotBeRestarted()
    {
        var folder = Path.GetDirectoryName(_ui)!;
        string In(string name) => Path.Combine(folder, name);
        ProcessSnapshot[] processes =
        [
            Proc(10, _ui, $"\"{_ui}\" --tray"),
            Proc(11, _ui, $"\"{_ui}\" --tray"),                       // duplicate of 10
            Proc(12, In("elevated.exe"), elevated: true),
            Proc(13, In("unknown.exe"), elevated: null),
            Proc(14, In("hidden.exe"), commandLine: null).WithoutCommandLine(),
            Proc(15, In("service.exe"), session: 0),
            Proc(16, In("unins000.exe")),
            Proc(17, In("Update.exe")),
            Proc(18, In("helper.exe"), parent: 10),                  // child of a selected process
            Proc(Self, In("self.exe")),
            Proc(19, @"C:\Tools\wgetui.exe"),
            Proc(20, Path.Combine(_workDir, "Other", "other.exe")),
            Proc(21, In("sub") + Path.DirectorySeparatorChar + "worker.exe", parent: 4),
        ];

        var targets = RelaunchPlanner.Select(processes, folder, Self, Session, @"C:\Tools\wgetui.exe");

        Assert.Equal([_ui, In("sub") + Path.DirectorySeparatorChar + "worker.exe"], targets.Select(t => t.ExecutablePath));
        Assert.Equal("--tray", targets[0].Arguments);
    }

    [Fact]
    public void SiblingFolderWithTheSamePrefixDoesNotMatch()
    {
        var folder = Path.GetDirectoryName(_ui)!;
        var sibling = folder + "Beta";

        var targets = RelaunchPlanner.Select([Proc(10, Path.Combine(sibling, "app.exe"))], folder, Self, Session, null);

        Assert.Empty(targets);
    }

    [Theory]
    [InlineData("\"C:\\A B\\app.exe\" --x \"y z\"", "--x \"y z\"")]
    [InlineData("C:\\A\\app.exe -a -b", "-a -b")]
    [InlineData("\"C:\\A B\\app.exe\"", "")]
    [InlineData("app.exe", "")]
    public void ArgumentsDropTheProgram(string commandLine, string expected) =>
        Assert.Equal(expected, RelaunchPlanner.ArgumentsOf(commandLine));

    [Fact]
    public void BroadOrRelativeInstallFoldersAreRefused()
    {
        string[] broad = [@"C:\Program Files"];

        Assert.False(RelaunchPlanner.IsUsableInstallFolder(null, broad));
        Assert.False(RelaunchPlanner.IsUsableInstallFolder(@"C:\", broad));
        Assert.False(RelaunchPlanner.IsUsableInstallFolder(@"C:\Program Files\", broad));
        Assert.False(RelaunchPlanner.IsUsableInstallFolder(@"relative\dir", broad));
        Assert.True(RelaunchPlanner.IsUsableInstallFolder(@"C:\Program Files\7-Zip\", broad));
    }
}

internal static class ProcessSnapshotTestExtensions
{
    public static ProcessSnapshot WithoutCommandLine(this ProcessSnapshot snapshot) => snapshot with { CommandLine = null };
}
