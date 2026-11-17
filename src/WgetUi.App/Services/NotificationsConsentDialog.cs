using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using WgetUi.App.Localization;

namespace WgetUi.App.Services;

/// <summary>First-run question: "Notify me when new updates are available?" Yes/No; closing it counts as No.</summary>
public static class NotificationsConsentDialog
{
    public static Task<bool> AskAsync() => Dispatcher.UIThread.InvokeAsync(ShowAsync);

    private static async Task<bool> ShowAsync()
    {
        var loc = Loc.Instance;
        var dialog = new FAContentDialog
        {
            Title = loc["Consent_Notify_Title"],
            Content = new TextBlock { Text = loc["Consent_Notify_Message"], TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = loc["Common_Yes"],
            CloseButtonText = loc["Common_No"],
            DefaultButton = FAContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == FAContentDialogResult.Primary;
    }
}
