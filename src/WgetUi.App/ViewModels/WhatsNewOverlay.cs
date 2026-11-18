using CommunityToolkit.Mvvm.ComponentModel;

namespace WgetUi.App.ViewModels;

/// <summary>
/// The "What's new" popup as an in-window modal overlay, styled like the package details one (MainWindow shows
/// <see cref="Current"/> over everything). Opening it again replaces what is shown. UI thread only.
/// </summary>
public sealed partial class WhatsNewOverlay : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial WhatsNewViewModel? Current { get; private set; }

    public bool IsOpen => Current is not null;

    public void Show(WhatsNewViewModel content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Current is { } previous)
        {
            previous.CloseRequested -= OnCloseRequested;
        }

        content.CloseRequested += OnCloseRequested;
        Current = content;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (sender is WhatsNewViewModel content)
        {
            content.CloseRequested -= OnCloseRequested;
            if (Current == content)
            {
                Current = null;
            }
        }
    }
}
