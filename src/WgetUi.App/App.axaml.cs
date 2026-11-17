using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.App.ViewModels;
using WgetUi.App.Views;

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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            LogWindowCreated(logger);
            _ = InitializeAsync(vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeAsync(MainWindowViewModel vm)
    {
        try
        {
            await vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Managers report their own failures; anything reaching here is a bug, keep the app usable.
            LogStartupFailed(_services.GetRequiredService<ILogger<App>>(), ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "wgetui {Version} starting")]
    private static partial void LogStarting(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Main window created")]
    private static partial void LogWindowCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Startup initialization failed")]
    private static partial void LogStartupFailed(ILogger logger, Exception ex);
}
