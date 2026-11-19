using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;
using WgetUi.App.Platform.Windows;
using WgetUi.App.Services;
using WgetUi.App.Services.AppUpdates;
using WgetUi.App.Services.Changelog;
using WgetUi.App.ViewModels;
using WgetUi.Core;
using WgetUi.Core.Managers;
using WgetUi.Core.Operations;
using WgetUi.Managers.WinGet;

namespace WgetUi.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Held by every process, primary or not, until it exits: the installer's AppMutex check looks for it.
        using var installerMutex = InstallerMutex.Acquire();
        var paths = AppPaths.FromArgs(args);
        using var instance = SingleInstance.Acquire(SingleInstance.KeyFor(paths.DataDirectory));
        if (!instance.IsPrimary)
        {
            // Already running for this data directory: bring it forward (unless this is a --tray start), hand over the
            // --import file if any, and leave.
            instance.Send(InstanceMessage.For(paths), TimeSpan.FromSeconds(3));
            return 0;
        }

        WindowsToastNotifier.SetProcessAppUserModelId();
        var services = ConfigureServices(paths, instance);
        installerMutex.LogFailures(services.GetRequiredService<ILogger<InstallerMutex>>());

        // A downloaded update not installed yet installs now, before any UI (installed copy only; see
        // StartupUpdateInstaller). The setup waits for this process, and so for the installer mutex, to go. Not when
        // the user opened a package list: the update waits for the next start instead of swallowing the file.
        if (paths.ImportPath is null && services.GetRequiredService<StartupUpdateInstaller>().TryStart(paths.StartInTray))
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return 0;
        }

        try
        {
            return AppBuilder.Configure(() => new App(services))
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Stops the operation queue worker and releases the winget COM objects.
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static ServiceProvider ConfigureServices(AppPaths paths, SingleInstance instance)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddDebug()
            .AddProvider(new FileLoggerProvider(paths.LogFile)));
        services.AddSingleton(paths);
        services.AddSingleton(instance);

        // Package managers: one line each. The rest of the app only sees IPackageManager.
        services.AddSingleton<IPackageManager, WinGetManager>();

        // Platform (Windows): the only place that touches the registry, toasts and processes.
        services.AddSingleton<WindowsToastNotifier>();
        services.AddSingleton<IToastNotifier>(sp => sp.GetRequiredService<WindowsToastNotifier>());
        services.AddSingleton<IRunKey, HkcuRunKey>();
        services.AddSingleton<ITaskbarTheme, RegistryTaskbarTheme>();
        services.AddSingleton<IProcessSource, WindowsProcessSource>();
        services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();
        services.AddSingleton<WindowsTaskbarFlasher>();
        services.AddSingleton<IWindowFlasher>(sp => sp.GetRequiredService<WindowsTaskbarFlasher>());
        services.AddSingleton<IAppInstallInfo, RegistryAppInstallInfo>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ManagerRegistry>();
        services.AddSingleton(RelaunchOptions.ForCurrentProcess());
        services.AddSingleton<IOperationHook, RelaunchAfterUpgrade>();
        services.AddSingleton<OperationQueue>();
        services.AddSingleton<IAgreementsConfirmer, AgreementsDialog>();
        services.AddSingleton<IQuitConfirmer, QuitConfirmDialog>();
        services.AddSingleton<OperationRequester>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton(sp => new StartWithWindowsService(sp.GetRequiredService<IRunKey>(), paths, sp.GetRequiredService<ILogger<StartWithWindowsService>>()));
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IgnoredUpdatesService>();
        services.AddSingleton<AutoUpdateService>();
        services.AddSingleton<PackageActions>();
        services.AddSingleton<DetailsOverlay>();
        services.AddSingleton<IPackageDetailsPresenter>(sp => sp.GetRequiredService<DetailsOverlay>());
        services.AddSingleton(sp => new PackageDetailsLauncher(
            sp.GetRequiredService<ManagerRegistry>(),
            sp.GetRequiredService<IPackageDetailsPresenter>(),
            sp.GetRequiredService<ILogger<PackageDetailsViewModel>>(),
            sp.GetRequiredService<AutoUpdateService>()));
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<UpdatesViewModel>();
        services.AddSingleton<IUninstallConfirmer, UninstallConfirmDialog>();
        services.AddSingleton<InstalledViewModel>();
        services.AddSingleton<OperationsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<ShellCoordinator>();
        services.AddSingleton<UpdateNotifier>();
        services.AddSingleton<OpenAppDetector>();
        services.AddSingleton<AutoUpdater>();
        services.AddSingleton<HourlyTicker>();
        services.AddSingleton<IUpdateCheckTicker>(sp => sp.GetRequiredService<HourlyTicker>());
        services.AddSingleton<UpdateCheckScheduler>();
        services.AddSingleton<AdminPromptWatcher>();

        // wgetui's own updates (GitHub Releases; swap IAppUpdateSource to change where they come from).
        services.AddSingleton(AppUpdateOptions.ForCurrentProcess(paths));
        services.AddSingleton(_ => AppUpdateHttp.CreateClient());
        services.AddSingleton<IAppUpdateSource, GitHubReleaseSource>();
        services.AddSingleton<IAppUpdateDownloader, AppUpdateDownloader>();
        services.AddSingleton<StartupUpdateInstaller>();
        services.AddSingleton<IUpdateRestartConfirmer, UpdateRestartDialog>();
        services.AddSingleton<AppUpdateViewModel>();
        services.AddSingleton<IAppUpdateChecker>(sp => sp.GetRequiredService<AppUpdateViewModel>());
        services.AddSingleton<IChangelogSource, EmbeddedChangelogSource>();
        services.AddSingleton<WhatsNewOverlay>();
        services.AddSingleton<WhatsNewLauncher>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
