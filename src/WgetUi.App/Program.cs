using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services;
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
        var paths = AppPaths.FromArgs(args);
        var services = ConfigureServices(paths);
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

    private static ServiceProvider ConfigureServices(AppPaths paths)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddDebug()
            .AddProvider(new FileLoggerProvider(paths.LogFile)));
        services.AddSingleton(paths);

        // Package managers: one line each. The rest of the app only sees IPackageManager.
        services.AddSingleton<IPackageManager, WinGetManager>();

        services.AddSingleton<ManagerRegistry>();
        services.AddSingleton<OperationQueue>();
        services.AddSingleton<IAgreementsConfirmer, AgreementsDialog>();
        services.AddSingleton<OperationRequester>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<PackageActions>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<UpdatesViewModel>();
        services.AddSingleton<OperationsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
