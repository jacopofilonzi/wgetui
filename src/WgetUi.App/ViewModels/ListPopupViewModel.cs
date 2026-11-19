using CommunityToolkit.Mvvm.Input;

namespace WgetUi.App.ViewModels;

/// <summary>
/// Base of the package-list popups (export, import, import from URL), shown by <see cref="PackageListOverlay"/>.
/// </summary>
/// <remarks>
/// Like the details popup, an action that may open another modal (installs: the agreements one) never runs while
/// the popup is open: it is stored with <see cref="Choose"/>, the popup closes, and the overlay runs it afterwards.
/// </remarks>
public abstract partial class ListPopupViewModel : ViewModelBase
{
    /// <summary>Raised when the popup should close (Close, Cancel, X, Escape, or an action was chosen).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The action to run once the popup has closed, or null.</summary>
    public Func<Task>? ChosenAction { get; private set; }

    /// <summary>Started when the popup opens; cancelled when it closes.</summary>
    public virtual Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs the chosen action, if any (called by the overlay after the popup closed).</summary>
    public Task RunChosenActionAsync() => ChosenAction?.Invoke() ?? Task.CompletedTask;

    protected void Choose(Func<Task> action)
    {
        ChosenAction = action;
        RequestClose();
    }

    protected void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Close() => RequestClose();
}
