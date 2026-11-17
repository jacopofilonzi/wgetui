using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Platform;
using WgetUi.App.ViewModels;
using WgetUi.App.Views;

namespace WgetUi.App.Services;

/// <summary>
/// Applies the shell decisions to the real window and tray: show/hide, close-to-tray, Quit, activation from
/// a second launch or a notification click. Decisions come from <see cref="ShellCoordinator"/>; this class
/// only wires Avalonia objects. The app runs with an explicit shutdown, so a hidden window keeps it alive.
/// </summary>
public sealed partial class ShellHost : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindowViewModel _vm;
    private readonly ShellCoordinator _coordinator;
    private readonly ILogger<ShellHost> _logger;
    private readonly MainWindow _window;
    private TrayIconHost? _tray;
    private bool _quitting;
    private bool _closeInProgress;

    public ShellHost(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel vm, ShellCoordinator coordinator, ILogger<ShellHost> logger)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        _desktop = desktop;
        _vm = vm;
        _coordinator = coordinator;
        _logger = logger;
        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _window = new MainWindow { DataContext = vm };
        _window.Closing += OnWindowClosing;
        _window.Activated += (_, _) => _vm.Operations.SetWindowActive(true);
        _window.Deactivated += (_, _) => _vm.Operations.SetWindowActive(false);
    }

    /// <summary>Raised on the UI thread the first time the window becomes visible.</summary>
    public event EventHandler? WindowFirstShown;

    public bool IsWindowVisible => _window.IsVisible;

    public void Start(bool startInTray, TrayViewModel tray, ITaskbarTheme taskbarTheme)
    {
        _tray = new TrayIconHost(tray, taskbarTheme, ShowWindow, () => _ = QuitAsync());
        if (!startInTray)
        {
            ShowWindow();
        }
        else
        {
            LogStartedInTray();
        }
    }

    /// <summary>Shows and activates the window (from the tray, a second launch or a notification). Any thread.</summary>
    public void ShowWindow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowWindow);
            return;
        }

        if (_desktop.MainWindow is null)
        {
            _desktop.MainWindow = _window;
            _window.Opened += FirstShown;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    /// <summary>The main window (platform handle for taskbar flashing).</summary>
    public MainWindow Window => _window;

    public async Task QuitAsync()
    {
        if (_quitting)
        {
            return;
        }

        if (_vm.Operations.HasPending)
        {
            // The confirmation is a modal on the window.
            ShowWindow();
        }

        if (await _coordinator.TryQuitAsync().ConfigureAwait(true))
        {
            Shutdown();
        }
    }

    public void Dispose() => _tray?.Dispose();

    private void FirstShown(object? sender, EventArgs e)
    {
        _window.Opened -= FirstShown;
        WindowFirstShown?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Our own shutdown, or Windows logging off: let it close.
        if (_quitting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (!_closeInProgress)
        {
            _ = HandleCloseAsync();
        }
    }

    private async Task HandleCloseAsync()
    {
        _closeInProgress = true;
        try
        {
            switch (await _coordinator.OnCloseRequestedAsync().ConfigureAwait(true))
            {
                case CloseOutcome.Hide:
                    _window.Hide();
                    LogHidden();
                    break;
                case CloseOutcome.Quit:
                    Shutdown();
                    break;
                case CloseOutcome.Stay:
                    break;
            }
        }
        finally
        {
            _closeInProgress = false;
        }
    }

    private void Shutdown()
    {
        _quitting = true;
        LogQuitting();
        _tray?.Dispose();
        _desktop.Shutdown();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started in the tray (--tray): window hidden")]
    private partial void LogStartedInTray();

    [LoggerMessage(Level = LogLevel.Information, Message = "Window closed to the tray")]
    private partial void LogHidden();

    [LoggerMessage(Level = LogLevel.Information, Message = "Quitting")]
    private partial void LogQuitting();
}
