using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services;

namespace WgetUi.App.ViewModels;

/// <summary>Language, theme and agreements; every change is applied and saved immediately.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly bool _loading = true;

    public SettingsViewModel(SettingsService settings, ThemeService theme, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        _settings = settings;
        _theme = theme;
        LogFile = paths.LogFile;
        SelectedLanguage = Languages.FirstOrDefault(o => o.Value == settings.Current.Language) ?? Languages[0];
        SelectedTheme = Themes.FirstOrDefault(o => o.Value == settings.Current.Theme) ?? Themes[0];
        AutoAcceptAgreements = settings.Current.AutoAcceptAgreements;
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
    public partial bool SaveFailed { get; set; }

    public string SettingsFile => _settings.FilePath;

    public string LogFile { get; }

    public string Version { get; } =
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

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

    private void Save(Action<AppSettings> change) => SaveFailed = !_settings.Update(change);
}
