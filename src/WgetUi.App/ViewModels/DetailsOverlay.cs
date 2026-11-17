using CommunityToolkit.Mvvm.ComponentModel;

namespace WgetUi.App.ViewModels;

/// <summary>
/// The details popup as an in-window modal overlay (MainWindow shows <see cref="Current"/> over everything).
/// </summary>
/// <remarks>
/// An overlay rather than an FAContentDialog: the dialog's fixed command band and width don't fit the design (own
/// header and footer, 640 px, scrolling body). Loading starts on open and is cancelled on close; the chosen action
/// runs only after the overlay is gone, so an install can open the agreements modal. UI thread only.
/// </remarks>
public sealed partial class DetailsOverlay : ObservableObject, IPackageDetailsPresenter
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial PackageDetailsViewModel? Current { get; private set; }

    public bool IsOpen => Current is not null;

    public async Task ShowAsync(PackageDetailsViewModel details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Current?.CloseCommand.Execute(null);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClose(object? sender, EventArgs e) => closed.TrySetResult();
        details.CloseRequested += OnClose;
        using var loading = new CancellationTokenSource();
        Current = details;
        var load = details.LoadAsync(loading.Token);
        try
        {
            await closed.Task;
        }
        finally
        {
            details.CloseRequested -= OnClose;
            if (Current == details)
            {
                Current = null;
            }

            await loading.CancelAsync();
        }

        await load;
        await details.RunChosenActionAsync();
    }
}
