namespace WgetUi.App.Platform;

/// <summary>Draws attention to the app's taskbar button (flashes until the window is activated).</summary>
public interface IWindowFlasher
{
    void Flash();
}
