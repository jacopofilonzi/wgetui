using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;
using WgetUi.App.Services;
using WgetUi.App.Services.AppUpdates;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>Where the self-update stands; drives the sidebar item.</summary>
public enum AppUpdateState
{
    /// <summary>No newer version known: the item is hidden.</summary>
    None,

    /// <summary>A newer version exists: "Update wgetui", click downloads (an automatic check downloads by itself).</summary>
    Available,

    /// <summary>Downloading with progress: click cancels.</summary>
    Downloading,

    /// <summary>Downloaded and verified: "Restart to update", click installs.</summary>
    Ready,

    /// <summary>A download started by a click, the launch, or the install at startup failed: the error is shown, click retries.</summary>
    Failed,
}

/// <summary>Asks whether to restart for the update while operations are queued or running (they get cancelled).</summary>
public interface IUpdateRestartConfirmer
{
    Task<bool> ConfirmRestartAsync(int pendingCount, string version);
}

/// <summary>The scheduled part of the self-update, called by <see cref="UpdateCheckScheduler"/> on every tick.</summary>
public interface IAppUpdateChecker
{
    /// <summary>Checks for a newer wgetui unless the copy is not the installed one or the user turned checks off.</summary>
    Task CheckAutomaticallyAsync();
}

/// <summary>
/// Self-update of wgetui: check (startup, hourly, or "Check for updates" in Settings) → download → sidebar item
/// "Restart to update" → silent setup, then quit; the setup reopens the app. A downloaded update not installed
/// in this run is installed at the next start (<see cref="StartupUpdateInstaller"/>).
/// </summary>
/// <remarks>
/// Only the installed copy takes part (<see cref="AppUpdateOptions.IsInstalledCopy"/>): dev and portable copies
/// never check, download or show the item. An automatic check (<see cref="AppSettings.CheckAppUpdates"/> on)
/// that finds a newer version downloads it in the background right away; the manual check only offers it and
/// downloads on click. A background download that fails is logged and falls back to
/// <see cref="AppUpdateState.Available"/> (no notification): the next automatic check retries. Every verified
/// download is recorded in <see cref="AppSettings.PendingAppUpdate"/> (and kept by the startup cleanup); at the
/// next start it is either installed by <see cref="StartupUpdateInstaller"/> or, when that is not allowed (checks
/// off) or already failed, offered again here as Ready / Failed. Restarting with operations queued or running asks
/// first (<see cref="IUpdateRestartConfirmer"/>) and cancels them, and the setup file is verified again just before
/// it is started with <see cref="SetupCommand"/>; the setup (see installer/wgetui.iss) waits for this process to
/// exit. A launch failure keeps the app open in <see cref="AppUpdateState.Failed"/>. "wgetui X is ready" is
/// notified once per version, after a background download, only when update notifications are on. Runs on the UI
/// thread.
/// </remarks>
public sealed partial class AppUpdateViewModel : ViewModelBase, IAppUpdateChecker
{
    private readonly IAppUpdateSource _source;
    private readonly IAppUpdateDownloader _downloader;
    private readonly AppUpdateOptions _options;
    private readonly IProcessLauncher _launcher;
    private readonly OperationQueue _queue;
    private readonly IUpdateRestartConfirmer _confirmer;
    private readonly SettingsService _settings;
    private readonly IToastNotifier _notifier;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<AppUpdateViewModel> _logger;
    private CancellationTokenSource? _download;
    private string? _downloadedPath;
    private bool _installing;
    private CheckOutcome _outcome;
    private string? _checkError;

    public AppUpdateViewModel(
        IAppUpdateSource source,
        IAppUpdateDownloader downloader,
        IAppInstallInfo installInfo,
        AppUpdateOptions options,
        IProcessLauncher launcher,
        OperationQueue queue,
        IUpdateRestartConfirmer confirmer,
        SettingsService settings,
        IToastNotifier notifier,
        IUiDispatcher dispatcher,
        ILogger<AppUpdateViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(installInfo);
        ArgumentNullException.ThrowIfNull(options);
        _source = source;
        _downloader = downloader;
        _options = options;
        _launcher = launcher;
        _queue = queue;
        _confirmer = confirmer;
        _settings = settings;
        _notifier = notifier;
        _dispatcher = dispatcher;
        _logger = logger;
        IsSupported = options.IsInstalledCopy(installInfo.InstallLocation);
        CheckCommand = new AsyncRelayCommand(() => CheckAsync(manual: true), () => IsSupported && !IsChecking);
    }

    private enum CheckOutcome
    {
        NotChecked,
        UpToDate,
        Available,
        Error,
    }

    /// <summary>Raised once the setup was started: the app must quit now, without asking again.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>This is the installed copy: only it checks for and installs updates.</summary>
    public bool IsSupported { get; }

    /// <summary>Running version, x.y.z.</summary>
    public string CurrentVersion => _options.CurrentVersion.ToString(3);

    [ObservableProperty]
    public partial AppUpdateState State { get; private set; }

    /// <summary>The newer release on offer (null in <see cref="AppUpdateState.None"/>).</summary>
    [ObservableProperty]
    public partial AppRelease? Latest { get; private set; }

    /// <summary>Download progress, 0–100.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    /// <summary>What failed in <see cref="AppUpdateState.Failed"/> (an <see cref="ErrorCodes"/> value).</summary>
    [ObservableProperty]
    public partial string? ErrorCode { get; private set; }

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    /// <summary>The sidebar item is shown.</summary>
    public bool IsVisible => IsSupported && State != AppUpdateState.None;

    public bool IsDownloading => State == AppUpdateState.Downloading;

    public string Title => State switch
    {
        AppUpdateState.Downloading => Loc["AppUpdate_Downloading"],
        AppUpdateState.Ready => Loc["AppUpdate_Ready"],
        AppUpdateState.Failed => Loc["AppUpdate_Retry"],
        _ => Loc["AppUpdate_Available"],
    };

    public string Subtitle => State switch
    {
        AppUpdateState.Downloading => Loc["AppUpdate_ClickToCancel"],
        AppUpdateState.Failed => Loc.Error(ErrorCode),
        _ => Loc.Format("AppUpdate_Version", Latest?.VersionText),
    };

    /// <summary>Both lines, for the compact pane where only the icon shows.</summary>
    public string ToolTipText => Title + Environment.NewLine + Subtitle;

    /// <summary>Result of the last check, for Settings (null before the first one).</summary>
    public string? CheckResult => IsChecking ? Loc["Settings_AppUpdateChecking"] : _outcome switch
    {
        CheckOutcome.UpToDate => Loc["Settings_AppUpToDate"],
        CheckOutcome.Available when State == AppUpdateState.Ready => Loc.Format("Settings_AppUpdateReady", Latest?.VersionText),
        CheckOutcome.Available => Loc.Format("Settings_AppUpdateAvailable", Latest?.VersionText),
        CheckOutcome.Error => Loc.Error(_checkError),
        _ => null,
    };

    /// <summary>Settings' "Check for updates": works even with automatic checks off.</summary>
    public IAsyncRelayCommand CheckCommand { get; }

    /// <summary>
    /// At startup: offers again a downloaded update that was not installed, clears the other setups left by
    /// earlier runs, then the first automatic check.
    /// </summary>
    public async Task StartAsync()
    {
        if (!IsSupported)
        {
            LogNotInstalledCopy(_options.ExecutableDirectory);
            return;
        }

        RestorePending();
        _downloader.DeleteOldSetups(_downloadedPath is null ? null : Latest?.AssetName);
        await CheckAutomaticallyAsync();
    }

    public Task CheckAutomaticallyAsync() =>
        IsSupported && _settings.Current.CheckAppUpdates ? CheckAsync(manual: false) : Task.CompletedTask;

    /// <summary>The sidebar item was clicked: download, cancel, install or retry, depending on <see cref="State"/>.</summary>
    public Task ActivateAsync()
    {
        switch (State)
        {
            case AppUpdateState.Available:
                return DownloadAsync();
            case AppUpdateState.Downloading:
                _download?.Cancel();
                return Task.CompletedTask;
            case AppUpdateState.Ready:
                return InstallAsync();
            case AppUpdateState.Failed:
                return _downloadedPath is null ? DownloadAsync() : InstallAsync();
            default:
                return Task.CompletedTask;
        }
    }

    public override void OnLanguageChanged() => RaiseTexts();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(State) or nameof(Latest) or nameof(ErrorCode))
        {
            RaiseTexts();
        }
        else if (e.PropertyName == nameof(IsChecking))
        {
            OnPropertyChanged(nameof(CheckResult));
            CheckCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task CheckAsync(bool manual)
    {
        if (!IsSupported || IsChecking)
        {
            return;
        }

        if (State is AppUpdateState.Downloading or AppUpdateState.Ready || (State == AppUpdateState.Failed && _downloadedPath is not null))
        {
            // Already on its way: a check would only disturb the download or the ready file.
            _outcome = CheckOutcome.Available;
            OnPropertyChanged(nameof(CheckResult));
            return;
        }

        IsChecking = true;
        var download = false;
        try
        {
            var release = await _source.GetLatestAsync(CancellationToken.None);
            if (release is null || release.Version <= _options.CurrentVersion)
            {
                _outcome = CheckOutcome.UpToDate;
                if (State != AppUpdateState.None)
                {
                    // The release was withdrawn: nothing to offer any more.
                    Reset(null, AppUpdateState.None);
                }

                return;
            }

            _outcome = CheckOutcome.Available;
            if (State == AppUpdateState.None || Latest?.Version != release.Version)
            {
                Reset(release, AppUpdateState.Available);
                LogAvailable(release.VersionText);
            }

            // Automatic checks download right away (and retry a failed download); the manual one only offers it.
            download = !manual && State is AppUpdateState.Available or AppUpdateState.Failed;
        }
        catch (AppUpdateException ex)
        {
            _outcome = CheckOutcome.Error;
            _checkError = ex.ErrorCode;
            LogCheckFailed(ex.ErrorCode, ex);
        }
        finally
        {
            IsChecking = false;
        }

        if (download)
        {
            await DownloadAsync(background: true);
        }
    }

    /// <summary>A verified download from an earlier run that was not installed: Ready again, or Failed when its install already failed.</summary>
    private void RestorePending()
    {
        var release = StartupUpdateInstaller.ReadPending(_settings.Current.PendingAppUpdate);
        if (release is null || release.Version <= _options.CurrentVersion)
        {
            return;
        }

        var path = Path.Combine(_options.DownloadDirectory, release.AssetName);
        if (!_downloader.Verify(path, release))
        {
            _settings.Update(s => s.PendingAppUpdate = null);
            return;
        }

        Latest = release;
        _downloadedPath = path;
        _outcome = CheckOutcome.Available;
        if (string.Equals(_settings.Current.AppUpdateAutoInstallAttempted, release.VersionText, StringComparison.Ordinal))
        {
            Fail(ErrorCodes.AppUpdateInstallFailed, null);
        }
        else
        {
            State = AppUpdateState.Ready;
        }
    }

    private void Reset(AppRelease? release, AppUpdateState state)
    {
        Latest = release;
        _downloadedPath = null;
        ErrorCode = null;
        State = state;
    }

    private void AnnounceReady(AppRelease release)
    {
        var version = release.VersionText;
        if (_settings.Current.NotifyNewUpdates != true
            || string.Equals(_settings.Current.AppUpdateNotifiedVersion, version, StringComparison.Ordinal))
        {
            return;
        }

        _notifier.Show(Loc.Format("Toast_AppUpdateReady_Title", version), Loc["Toast_AppUpdateReady_Body"], ToastAction.OpenWindow);
        _settings.Update(s => s.AppUpdateNotifiedVersion = version);
        LogNotified(version);
    }

    private async Task DownloadAsync(bool background = false)
    {
        if (Latest is not { } release || _download is not null)
        {
            return;
        }

        using var cancel = new CancellationTokenSource();
        _download = cancel;
        Progress = 0;
        IsProgressIndeterminate = true;
        ErrorCode = null;
        State = AppUpdateState.Downloading;
        try
        {
            var path = await _downloader.DownloadAsync(release, new DispatchedProgress(this), cancel.Token);
            _downloadedPath = path;
            _settings.Update(s => s.PendingAppUpdate = StartupUpdateInstaller.ToRecord(release));
            State = AppUpdateState.Ready;
            if (background)
            {
                AnnounceReady(release);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            LogDownloadCancelled(release.VersionText);
            State = AppUpdateState.Available;
        }
        catch (AppUpdateException ex) when (background)
        {
            // Nobody clicked: no error on screen, the next automatic check tries again.
            LogBackgroundDownloadFailed(release.VersionText, ex.ErrorCode, ex);
            State = AppUpdateState.Available;
        }
        catch (AppUpdateException ex)
        {
            Fail(ex.ErrorCode, ex);
        }
        finally
        {
            _download = null;
        }
    }

    private async Task InstallAsync()
    {
        if (_installing || _downloadedPath is not { } path || Latest is not { } release)
        {
            return;
        }

        _installing = true;
        try
        {
            var pending = _queue.Operations.Count(o => !o.IsFinished);
            if (pending > 0 && !await _confirmer.ConfirmRestartAsync(pending, release.VersionText))
            {
                LogRestartDeclined(pending);
                return;
            }

            if (!_downloader.Verify(path, release))
            {
                _downloadedPath = null;
                Fail(ErrorCodes.AppUpdateHashMismatch, null);
                return;
            }

            // Recorded first: if this install fails, the next start doesn't start it again by itself.
            _settings.Update(s => s.AppUpdateAutoInstallAttempted = release.VersionText);
            try
            {
                _launcher.Start(path, SetupCommand.Arguments(_options.DownloadDirectory, tray: false), _options.DownloadDirectory);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Fail(ErrorCodes.AppUpdateLaunchFailed, ex);
                return;
            }

            var cancelled = 0;
            foreach (var operation in _queue.Operations.Where(o => !o.IsFinished).ToList())
            {
                _queue.Cancel(operation);
                cancelled++;
            }

            LogSetupStarted(release.VersionText, cancelled);
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _installing = false;
        }
    }

    private void Fail(string code, Exception? ex)
    {
        ErrorCode = code;
        State = AppUpdateState.Failed;
        LogFailed(code, ex);
    }

    private void RaiseTexts()
    {
        foreach (var name in new[] { nameof(IsVisible), nameof(IsDownloading), nameof(Title), nameof(Subtitle), nameof(ToolTipText), nameof(CheckResult) })
        {
            OnPropertyChanged(name);
        }
    }

    private void OnProgress(double? percent)
    {
        IsProgressIndeterminate = percent is null;
        Progress = percent ?? 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Self-update off: {Directory} is not the installed copy")]
    private partial void LogNotInstalledCopy(string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "wgetui {Version} is available")]
    private partial void LogAvailable(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background download of wgetui {Version} failed ({Code}); retried at the next automatic check")]
    private partial void LogBackgroundDownloadFailed(string version, string code, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notified wgetui {Version} as ready")]
    private partial void LogNotified(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "wgetui update check failed ({Code})")]
    private partial void LogCheckFailed(string code, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Download of wgetui {Version} cancelled")]
    private partial void LogDownloadCancelled(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "wgetui update failed ({Code})")]
    private partial void LogFailed(string code, Exception? ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restart to update declined: {Count} operation(s) pending")]
    private partial void LogRestartDeclined(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the wgetui {Version} setup (cancelled {Count} operation(s)); quitting")]
    private partial void LogSetupStarted(string version, int count);

    /// <summary>Download progress back on the UI thread.</summary>
    private sealed class DispatchedProgress(AppUpdateViewModel owner) : IProgress<double?>
    {
        public void Report(double? value) => owner._dispatcher.Post(() => owner.OnProgress(value));
    }
}
