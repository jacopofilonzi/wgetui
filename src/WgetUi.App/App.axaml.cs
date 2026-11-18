using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WgetUi.App.Icons;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Platform;
using WgetUi.App.Platform.Windows;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;

namespace WgetUi.App;

public sealed partial class App : Application
{
    private static readonly string AppVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "?";
    private readonly IServiceProvider _services;

    public App(IServiceProvider services) => _services = services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        LogStarting(logger, AppVersion);
        var settings = _services.GetRequiredService<SettingsService>().Current;
        Loc.Instance.SetLanguage(settings.Language);
        _services.GetRequiredService<ThemeService>().Apply(settings.Theme);
        RegisterNotifications(logger);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = _services.GetRequiredService<AppPaths>();
            var vm = _services.GetRequiredService<MainWindowViewModel>();
            var coordinator = _services.GetRequiredService<ShellCoordinator>();
            coordinator.TrayDisabled = paths.TrayDisabled;
            var shell = new ShellHost(desktop, vm, coordinator, _services.GetRequiredService<ILogger<ShellHost>>());
            var notifier = _services.GetRequiredService<UpdateNotifier>();
            shell.WindowActiveChanged += (_, active) => notifier.IsWindowActive = active;
            shell.WindowFirstShown += (_, _) => _ = AskNotificationConsentAsync(notifier, vm.Settings);
            var scheduler = _services.GetRequiredService<UpdateCheckScheduler>();
            _ = _services.GetRequiredService<AdminPromptWatcher>();
            var flasher = _services.GetRequiredService<WindowsTaskbarFlasher>();
            shell.Window.Opened += (_, _) => flasher.Attach(shell.Window.TryGetPlatformHandle()?.Handle ?? 0);
            shell.Start(paths.StartInTray, paths.TrayDisabled, _services.GetRequiredService<TrayViewModel>(), _services.GetRequiredService<ITaskbarTheme>());
            desktop.Exit += (_, _) => shell.Dispose();

            var instance = _services.GetRequiredService<SingleInstance>();
            instance.MessageReceived += (_, message) =>
            {
                if (message == SingleInstance.ActivateMessage)
                {
                    shell.ShowWindow();
                }
            };
            instance.Listen(logger);
            _services.GetRequiredService<IToastNotifier>().Activated += (_, action) => shell.ShowWindow(action == ToastAction.OpenUpdates ? MainWindowViewModel.UpdatesPage : null);

            LogWindowCreated(logger);
            _ = InitializeAsync(vm, scheduler);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The toast icon must be a file: render the app icon once into the local app folder (not the data dir,
    // so a scratch --data-dir never leaves the shared registration pointing at a deleted file).
    private void RegisterNotifications(ILogger logger)
    {
        string? iconPath = null;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wgetui");
            Directory.CreateDirectory(dir);
            iconPath = Path.Combine(dir, "notification-icon.png");
            File.WriteAllBytes(iconPath, IconRenderer.RenderPng(256, c => IconArt.DrawApp(c, 256)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogIconWriteFailed(logger, ex);
            iconPath = null;
        }

        _services.GetRequiredService<WindowsToastNotifier>().Register(iconPath);
    }

    private async Task InitializeAsync(MainWindowViewModel vm, UpdateCheckScheduler scheduler)
    {
        try
        {
            // The first refresh is the startup update check; the scheduler takes over hourly.
            await vm.InitializeAsync();
            await scheduler.RunAutomaticUpdatesAsync();
            scheduler.Start();
        }
        catch (Exception ex)
        {
            // Managers report their own failures; anything reaching here is a bug, keep the app usable.
            LogStartupFailed(_services.GetRequiredService<ILogger<App>>(), ex);
        }
    }

    // First run only: asked once the window is visible (a --tray start waits until the user opens it).
    private static async Task AskNotificationConsentAsync(UpdateNotifier notifier, SettingsViewModel settings)
    {
        if (notifier.NeedsConsent)
        {
            settings.AnswerNotifyQuestion(await NotificationsConsentDialog.AskAsync());
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "wgetui {Version} starting")]
    private static partial void LogStarting(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Main window created")]
    private static partial void LogWindowCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Startup initialization failed")]
    private static partial void LogStartupFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the notification icon")]
    private static partial void LogIconWriteFailed(ILogger logger, Exception ex);
}
