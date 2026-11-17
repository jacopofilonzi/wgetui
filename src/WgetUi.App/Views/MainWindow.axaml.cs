using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using WgetUi.App.ViewModels;

namespace WgetUi.App.Views;

/// <summary>View wiring only: navigation selection → view model, Mica fallback.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Nav.SelectionChanged += OnNavigationChanged;
        Nav.SelectedItem = UpdatesItem;
        Opened += (_, _) => ApplyBackdrop();
    }

    /// <summary>Selects a page through the navigation view, so the selected item and the page stay in sync.</summary>
    public void SelectPage(string page)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<FANavigationViewItem>().FirstOrDefault(i => i.Tag as string == page);
        if (item is not null)
        {
            Nav.SelectedItem = item;
        }
    }

    private void OnNavigationChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.SelectedItem is FANavigationViewItem { Tag: string page })
        {
            vm.Navigate(page);
        }
    }

    // Mica needs a transparent window; where it is unavailable keep the theme's solid background.
    private void ApplyBackdrop()
    {
        if (ActualTransparencyLevel == WindowTransparencyLevel.Mica)
        {
            Background = Avalonia.Media.Brushes.Transparent;
        }
    }
}
