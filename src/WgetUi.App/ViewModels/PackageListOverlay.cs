using CommunityToolkit.Mvvm.ComponentModel;

namespace WgetUi.App.ViewModels;

/// <summary>
/// The package-list popups (export, import, import from URL) as an in-window modal overlay, styled like the details
/// popup (MainWindow shows <see cref="Current"/> over everything). Opening one closes the previous one. Loading starts
/// on open and is cancelled on close; the chosen action runs once the popup is gone. UI thread only.
/// </summary>
public sealed partial class PackageListOverlay : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial ListPopupViewModel? Current { get; private set; }

    public bool IsOpen => Current is not null;

    /// <summary>Shows the popup and returns once it closed and its chosen action (if any) ran.</summary>
    public async Task ShowAsync(ListPopupViewModel popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        Current?.CloseCommand.Execute(null);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClose(object? sender, EventArgs e) => closed.TrySetResult();
        popup.CloseRequested += OnClose;
        using var loading = new CancellationTokenSource();
        Current = popup;
        var load = popup.LoadAsync(loading.Token);
        try
        {
            await closed.Task;
        }
        finally
        {
            popup.CloseRequested -= OnClose;
            if (Current == popup)
            {
                Current = null;
            }

            await loading.CancelAsync();
        }

        try
        {
            await load;
        }
        catch (OperationCanceledException)
        {
            // Closed while loading.
        }

        await popup.RunChosenActionAsync();
    }
}
