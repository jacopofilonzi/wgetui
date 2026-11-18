using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services;

namespace WgetUi.App.ViewModels;

/// <summary>Language, theme, agreements and background behaviour; every change is applied and saved immediately.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly StartWithWindowsService _startWithWindows;
    private readonly IgnoredUpdatesService _ignored;
    private readonly WhatsNewLauncher _whatsNew;
    private readonly bool _loading = true;

    public SettingsViewModel(SettingsService settings, ThemeService theme, StartWithWindowsService startWithWindows, IgnoredUpdatesService ignored, AppPaths paths, AppUpdateViewModel appUpdate, WhatsNewLauncher whatsNew)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(ignored);
        _settings = settings;
        AppUpdate = appUpdate;
        _whatsNew = whatsNew;
        _ignored = ignored;
        ignored.Changed += (_, _) => LoadIgnored();
        LoadIgnored();
        _theme = theme;
        _startWithWindows = startWithWindows;
        LogFile = paths.LogFile;
        SelectedLanguage = Languages.FirstOrDefault(o => o.Value == settings.Current.Language) ?? Languages[0];
        SelectedTheme = Themes.FirstOrDefault(o => o.Value == settings.Current.Theme) ?? Themes[0];
        AutoAcceptAgreements = settings.Current.AutoAcceptAgreements;
        CloseToTray = settings.Current.CloseToTray;
        NotifyNewUpdates = settings.Current.NotifyNewUpdates == true;
        RelaunchClosedApps = settings.Current.RelaunchClosedApps;
        AutoUpdatesEnabled = settings.Current.AutoUpdatesEnabled;
        AutoUpdateOpenApps = settings.Current.AutoUpdateOpenApps;
        StartWithWindows = startWithWindows.IsEnabled;
        CheckAppUpdates = settings.Current.CheckAppUpdates;
        _loading = false;
    }

    public IReadOnlyList<ChoiceOption> Languages { get; } =
    [
        new(AppSettings.System, "Settings_Lang_System"),
        new(Localization.Loc.English, "Settings_Lang_En"),
        new(Localization.Loc.Italian, "Settings_Lang_It"),
    ];

    public IReadOnlyList<ChoiceOption> Themes { get; } =
    [
        new(AppSettings.System, "Settings_Theme_System"),
        new(ThemeService.Light, "Settings_Theme_Light"),
        new(ThemeService.Dark, "Settings_Theme_Dark"),
    ];

    [ObservableProperty]
    public partial ChoiceOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial ChoiceOption SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool AutoAcceptAgreements { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool NotifyNewUpdates { get; set; }

    [ObservableProperty]
    public partial bool RelaunchClosedApps { get; set; }

    /// <summary>Global switch of the per-app automatic updates (off pauses them all, keeping each app's choice).</summary>
    [ObservableProperty]
    public partial bool AutoUpdatesEnabled { get; set; }

    /// <summary>Automatic updates also for apps that are open, as if each had "Even if the app is open".</summary>
    [ObservableProperty]
    public partial bool AutoUpdateOpenApps { get; set; }

    /// <summary>Read from and written to the Run key, not settings.json.</summary>
    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    /// <summary>Automatic checks for a newer wgetui (the manual check works regardless).</summary>
    [ObservableProperty]
    public partial bool CheckAppUpdates { get; set; }

    [ObservableProperty]
    public partial bool SaveFailed { get; set; }

    /// <summary>About: current version, "Check for updates" and its result.</summary>
    public AppUpdateViewModel AppUpdate { get; }

    /// <summary>Ignored updates, each with "Restore".</summary>
    public ObservableCollection<IgnoredUpdateItemViewModel> IgnoredUpdates { get; } = [];

    public bool HasIgnoredUpdates => IgnoredUpdates.Count > 0;

    public string SettingsFile => _settings.FilePath;

    public string LogFile { get; }

    public string Version { get; } = AppVersion.Text;

    /// <summary>About > "What's new": the release notes of every version up to this one.</summary>
    [RelayCommand]
    private void ShowWhatsNew() => _whatsNew.ShowAll();

    public override void OnLanguageChanged()
    {
        foreach (var option in Languages.Concat(Themes))
        {
            option.Refresh();
        }
    }

    partial void OnSelectedLanguageChanged(ChoiceOption value)
    {
        if (_loading)
        {
            return;
        }

        Save(s => s.Language = value.Value);
        Loc.SetLanguage(value.Value);
    }

    partial void OnSelectedThemeChanged(ChoiceOption value)
    {
        if (_loading)
        {
            return;
        }

        Save(s => s.Theme = value.Value);
        _theme.Apply(value.Value);
    }

    partial void OnAutoAcceptAgreementsChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s.AutoAcceptAgreements = value);
        }
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s.CloseToTray = value);
        }
    }

    /// <summary>Stores the answer to the first-run question (also when it matches the current toggle).</summary>
    public void AnswerNotifyQuestion(bool notify)
    {
        Save(s => s.NotifyNewUpdates = notify);
        NotifyNewUpdates = notify;
    }

    partial void OnNotifyNewUpdatesChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s.NotifyNewUpdates = value);
        }
    }

    partial void OnRelaunchClosedAppsChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s.RelaunchClosedApps = value);
        }
    }

    partial void OnAutoUpdatesEnabledChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s.AutoUpdatesEnabled = value);
        }
    }

    partial void OnAutoUpdateOpenAppsChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s.AutoUpdateOpenApps = value);
        }
    }

    partial void OnCheckAppUpdatesChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s.CheckAppUpdates = value);
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_loading)
        {
            SaveFailed = !_startWithWindows.SetEnabled(value);
        }
    }

    private void Save(Action<AppSettings> change) => SaveFailed = !_settings.Update(change);

    private void LoadIgnored()
    {
        IgnoredUpdates.Clear();
        foreach (var record in _ignored.Records.OrderBy(r => r.Name ?? r.PackageId, StringComparer.CurrentCultureIgnoreCase))
        {
            IgnoredUpdates.Add(new IgnoredUpdateItemViewModel(record, r => SaveFailed = !_ignored.Restore(r)));
        }

        OnPropertyChanged(nameof(HasIgnoredUpdates));
    }
}
