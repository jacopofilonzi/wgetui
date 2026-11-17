namespace WgetUi.App.Platform;

/// <summary>Whether the taskbar (system theme, not the app theme) is light, to pick the tray glyph colour.</summary>
public interface ITaskbarTheme
{
    bool IsLight { get; }
}
