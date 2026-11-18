using CommunityToolkit.Mvvm.ComponentModel;
using WgetUi.Core;

namespace WgetUi.App.ViewModels;

/// <summary>Shell: navigation between pages, the queue panel and the "manager unavailable" banner.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    public const string SearchPage = "search";
    public const string UpdatesPage = "updates";
    public const string InstalledPage = "installed";
    public const string SettingsPage = "settings";

    private readonly ManagerRegistry _registry;
    private IReadOnlyList<ManagerError> _initErrors = [];

    public MainWindowViewModel(ManagerRegistry registry, SearchViewModel search, UpdatesViewModel updates, InstalledViewModel installed, OperationsViewModel operations, SettingsViewModel settings, DetailsOverlay details, AppUpdateViewModel appUpdate)
    {
        _registry = registry;
        Details = details;
        Search = search;
        Updates = updates;
        Installed = installed;
        Operations = operations;
        Settings = settings;
        AppUpdate = appUpdate;
        CurrentPage = updates;
        Loc.CultureChanged += (_, _) =>
        {
            foreach (var page in new ViewModelBase[] { this, Search, Updates, Installed, Operations, Settings, AppUpdate })
            {
                page.OnLanguageChanged();
            }
        };
    }

    public SearchViewModel Search { get; }

    public UpdatesViewModel Updates { get; }

    public InstalledViewModel Installed { get; }

    /// <summary>The package details popup, shown over the whole window while open.</summary>
    public DetailsOverlay Details { get; }

    public OperationsViewModel Operations { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>wgetui's own update: the sidebar item above Settings.</summary>
    public AppUpdateViewModel AppUpdate { get; }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    [ObservableProperty]
    public partial bool IsBannerOpen { get; set; }

    [ObservableProperty]
    public partial string? BannerMessage { get; set; }

    public void Navigate(string page)
    {
        CurrentPage = page switch
        {
            SearchPage => Search,
            InstalledPage => Installed,
            SettingsPage => Settings,
            _ => Updates,
        };

        if (CurrentPage == Installed)
        {
            Installed.EnsureLoaded();
        }
    }

    /// <summary>Connects every manager, reports the unavailable ones, then loads the updates.</summary>
    public async Task InitializeAsync()
    {
        _initErrors = await _registry.InitializeAsync(CancellationToken.None);
        ShowBanner();
        await Updates.RefreshCommand.ExecuteAsync(null);
    }

    public override void OnLanguageChanged() => ShowBanner();

    private void ShowBanner()
    {
        BannerMessage = DescribeErrors(_registry, _initErrors);
        IsBannerOpen = BannerMessage is not null;
    }
}
