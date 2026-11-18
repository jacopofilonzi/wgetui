using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;
using WgetUi.App.Services.AppUpdates;
using WgetUi.App.ViewModels;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

/// <summary>Scripted release source: never reaches GitHub.</summary>
internal sealed class FakeAppUpdateSource : IAppUpdateSource
{
    public AppRelease? Latest { get; set; }

    public Exception? Failure { get; set; }

    public int Calls { get; private set; }

    public static AppRelease Release(string version) =>
        new(Version.Parse(version), "v" + version, $"wgetui-setup-{version}-x64.exe", new Uri($"https://example.com/wgetui-setup-{version}-x64.exe"), 10, new string('a', 64));

    public Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Failure is not null ? Task.FromException<AppRelease?>(Failure) : Task.FromResult(Latest);
    }
}

/// <summary>Downloads that finish when the test says so. Never downloads or writes anything.</summary>
internal sealed class FakeAppUpdateDownloader : IAppUpdateDownloader
{
    private TaskCompletionSource<string>? _pending;

    public int Downloads { get; private set; }

    public IProgress<double?>? Progress { get; private set; }

    public bool VerifyResult { get; set; } = true;

    public int Cleanups { get; private set; }

    public Task<string> DownloadAsync(AppRelease release, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        Downloads++;
        Progress = progress;
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
        _pending = pending;
        return pending.Task;
    }

    public void Complete(string path) => _pending!.TrySetResult(path);

    public void Fail(Exception error) => _pending!.TrySetException(error);

    public bool Verify(string path, AppRelease release) => VerifyResult;

    public void DeleteOldSetups() => Cleanups++;
}

internal sealed class FakeInstallInfo(string? location) : IAppInstallInfo
{
    public string? InstallLocation { get; } = location;
}

/// <summary>Answers "Restart to update?" with <see cref="Answer"/> and records the questions.</summary>
internal sealed class FakeRestartConfirmer : IUpdateRestartConfirmer
{
    public bool Answer { get; set; }

    public List<(int Pending, string Version)> Asked { get; } = [];

    public Task<bool> ConfirmRestartAsync(int pendingCount, string version)
    {
        Asked.Add((pendingCount, version));
        return Task.FromResult(Answer);
    }
}

/// <summary>Counts the scheduler's calls.</summary>
internal sealed class FakeAppUpdateChecker : IAppUpdateChecker
{
    public int Checks { get; private set; }

    public Task CheckAutomaticallyAsync()
    {
        Checks++;
        return Task.CompletedTask;
    }
}

/// <summary>An <see cref="AppUpdateViewModel"/> on fakes only, running from a pretend install folder.</summary>
internal sealed class AppUpdateHarness
{
    public const string InstallDirectory = @"C:\Users\someone\AppData\Local\Programs\wgetui";
    public const string DownloadDirectory = @"C:\Users\someone\AppData\Local\wgetui\updates";

    public AppUpdateHarness(SettingsService settings, OperationQueue queue, bool installed = true)
    {
        var options = new AppUpdateOptions(new Version(0, 2, 0), InstallDirectory, DownloadDirectory);
        ViewModel = new AppUpdateViewModel(
            Source,
            Downloader,
            new FakeInstallInfo(installed ? InstallDirectory + @"\" : null),
            options,
            Launcher,
            queue,
            Confirmer,
            settings,
            Notifier,
            Dispatcher,
            NullLogger<AppUpdateViewModel>.Instance);
        ViewModel.QuitRequested += (_, _) => QuitRequests++;
    }

    public FakeAppUpdateSource Source { get; } = new();

    public FakeAppUpdateDownloader Downloader { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public FakeRestartConfirmer Confirmer { get; } = new();

    public FakeNotifier Notifier { get; } = new();

    public ManualDispatcher Dispatcher { get; } = new();

    public AppUpdateViewModel ViewModel { get; }

    public int QuitRequests { get; private set; }
}
