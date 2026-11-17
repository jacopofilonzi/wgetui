using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using WgetUi.App.ViewModels;
using WgetUi.App.Views;

namespace WgetUi.App.Services;

/// <summary>
/// The package details modal: a button-less <see cref="FAContentDialog"/> hosting <see cref="PackageDetailsView"/>
/// (its own header and footer). Starts loading on open, cancels it on close, then runs the chosen action, so an
/// install can open the agreements modal (one dialog at a time).
/// </summary>
public sealed class PackageDetailsDialog : IPackageDetailsPresenter
{
    public Task ShowAsync(PackageDetailsViewModel details) => Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(details));

    private static async Task ShowCoreAsync(PackageDetailsViewModel details)
    {
        var dialog = new FAContentDialog { Content = new PackageDetailsView { DataContext = details } };
        // The default max width (548) is narrower than the 640 px design.
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        void OnClose(object? sender, EventArgs e) => dialog.Hide();
        details.CloseRequested += OnClose;
        using var loading = new CancellationTokenSource();
        var load = details.LoadAsync(loading.Token);
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            details.CloseRequested -= OnClose;
            await loading.CancelAsync();
        }

        await load;
        await details.RunChosenActionAsync();
    }
}
