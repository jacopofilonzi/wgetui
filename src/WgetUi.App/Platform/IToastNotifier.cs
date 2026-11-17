namespace WgetUi.App.Platform;

/// <summary>What clicking a notification should do once the window is shown.</summary>
public enum ToastAction
{
    OpenWindow,
    OpenUpdates,
}

/// <summary>Shows desktop notifications (Windows toasts). Never throws: failures are logged.</summary>
public interface IToastNotifier
{
    /// <summary>Raised (on any thread) when the user clicks a notification while the app is running.</summary>
    event EventHandler<ToastAction>? Activated;

    void Show(string title, string body, ToastAction action = ToastAction.OpenWindow);
}
