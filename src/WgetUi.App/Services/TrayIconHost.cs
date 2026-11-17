using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using WgetUi.App.Icons;
using WgetUi.App.Platform;
using WgetUi.App.ViewModels;

namespace WgetUi.App.Services;

/// <summary>
/// The notification-area icon: renders <see cref="TrayViewModel"/> (glyph coloured for the taskbar theme, state
/// dot) and its menu. Icons are rendered once per (theme, state) and cached. UI thread only.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private static readonly Dictionary<TrayIconState, Color?> DotColors = new()
    {
        [TrayIconState.None] = null,
        [TrayIconState.Updates] = Color.Parse("#3B82F6"),
        [TrayIconState.Running] = Color.Parse("#16A34A"),
        [TrayIconState.AdminPrompt] = Color.Parse("#F59E0B"),
        [TrayIconState.Failure] = Color.Parse("#DC2626"),
    };

    private readonly TrayViewModel _vm;
    private readonly ITaskbarTheme _taskbarTheme;
    private readonly Dictionary<(bool Light, TrayIconState State), WindowIcon> _icons = [];
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _open = new();
    private readonly NativeMenuItem _upgradeAll = new();
    private readonly NativeMenuItem _check = new();
    private readonly NativeMenuItem _quit = new();
    private (bool Light, TrayIconState State)? _shown;

    public TrayIconHost(TrayViewModel vm, ITaskbarTheme taskbarTheme, Action open, Action quit)
    {
        ArgumentNullException.ThrowIfNull(vm);
        _vm = vm;
        _taskbarTheme = taskbarTheme;
        _open.Click += (_, _) => open();
        _upgradeAll.Click += (_, _) => _vm.UpgradeAll();
        _check.Click += (_, _) => _vm.CheckForUpdates();
        _quit.Click += (_, _) => quit();
        var menu = new NativeMenu();
        menu.Items.Add(_open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_upgradeAll);
        menu.Items.Add(_check);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_quit);
        _tray = new TrayIcon { Menu = menu };
        _tray.Clicked += (_, _) => open();
        if (Application.Current is { } app)
        {
            TrayIcon.SetIcons(app, [_tray]);
            if (app.PlatformSettings is { } settings)
            {
                // Fires on WM_SETTINGCHANGE, which also covers a taskbar (system) theme switch.
                settings.ColorValuesChanged += (_, _) => Refresh();
            }
        }

        _vm.Changed += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        var key = (_taskbarTheme.IsLight, _vm.State);
        if (_shown != key)
        {
            if (!_icons.TryGetValue(key, out var icon))
            {
                var glyph = key.IsLight ? IconArt.DarkGlyph : Colors.White;
                icon = new WindowIcon(new MemoryStream(IconRenderer.TrayIco(glyph, DotColors[key.State])));
                _icons[key] = icon;
            }

            _tray.Icon = icon;
            _shown = key;
        }

        _tray.ToolTipText = _vm.ToolTip;
        _open.Header = Localization.Loc.Instance["Tray_Open"];
        _upgradeAll.Header = _vm.UpgradeAllHeader;
        _upgradeAll.IsEnabled = _vm.CanUpgradeAll;
        _check.Header = Localization.Loc.Instance["Tray_CheckUpdates"];
        _check.IsEnabled = _vm.CanCheckForUpdates;
        _quit.Header = Localization.Loc.Instance["Tray_Quit"];
    }

    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
    }
}
