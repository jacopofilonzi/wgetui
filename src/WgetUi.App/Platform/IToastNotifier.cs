namespace WgetUi.App.Platform;

/// <summary>Shows desktop notifications (Windows toasts). Never throws: failures are logged.</summary>
public interface IToastNotifier
{
    /// <summary>Raised (on any thread) when the user clicks a notification while the app is running.</summary>
    event EventHandler? Activated;

    void Show(string title, string body);
}
