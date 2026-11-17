using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using WgetUi.App.Localization;
using WgetUi.App.ViewModels;

namespace WgetUi.App.Services;

/// <summary>The "quit with pending operations?" modal. The shell shows the window before calling it.</summary>
public sealed class QuitConfirmDialog : IQuitConfirmer
{
    public Task<bool> ConfirmQuitAsync(int pendingCount) => Dispatcher.UIThread.InvokeAsync(() => ShowAsync(pendingCount));

    private static async Task<bool> ShowAsync(int pendingCount)
    {
        var loc = Loc.Instance;
        var dialog = new FAContentDialog
        {
            Title = loc["Quit_Title"],
            Content = new TextBlock { Text = loc.Format("Quit_Message", pendingCount), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = loc["Quit_Confirm"],
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = FAContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == FAContentDialogResult.Primary;
    }
}
