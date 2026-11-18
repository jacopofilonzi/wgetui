using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using WgetUi.App.Localization;
using WgetUi.App.ViewModels;

namespace WgetUi.App.Services;

/// <summary>"Restart to update?" when operations are queued or running. Shown from a click, so the window is visible.</summary>
public sealed class UpdateRestartDialog : IUpdateRestartConfirmer
{
    public Task<bool> ConfirmRestartAsync(int pendingCount, string version) =>
        Dispatcher.UIThread.InvokeAsync(() => ShowAsync(pendingCount, version));

    private static async Task<bool> ShowAsync(int pendingCount, string version)
    {
        var loc = Loc.Instance;
        var dialog = new FAContentDialog
        {
            Title = loc["AppUpdate_Restart_Title"],
            Content = new TextBlock { Text = loc.Format("AppUpdate_Restart_Message", pendingCount, version), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = loc["AppUpdate_Restart_Confirm"],
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = FAContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == FAContentDialogResult.Primary;
    }
}
