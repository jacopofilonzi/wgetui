using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using WgetUi.App.Localization;
using WgetUi.App.ViewModels;
using WgetUi.Core.Models;

namespace WgetUi.App.Services;

/// <summary>The "Uninstall X?" modal; true only on an explicit Uninstall.</summary>
public sealed class UninstallConfirmDialog : IUninstallConfirmer
{
    public Task<bool> ConfirmUninstallAsync(Package package) => Dispatcher.UIThread.InvokeAsync(() => ShowAsync(package));

    private static async Task<bool> ShowAsync(Package package)
    {
        var loc = Loc.Instance;
        var dialog = new FAContentDialog
        {
            Title = loc.Format("Uninstall_Title", package.Name),
            Content = new TextBlock { Text = loc.Format("Uninstall_Message", package.Name, package.Id), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = loc["Installed_Uninstall"],
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = FAContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == FAContentDialogResult.Primary;
    }
}
