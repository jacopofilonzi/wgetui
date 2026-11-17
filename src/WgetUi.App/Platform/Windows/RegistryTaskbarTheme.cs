using Microsoft.Win32;

namespace WgetUi.App.Platform.Windows;

/// <summary>Reads SystemUsesLightTheme (taskbar/Start theme; AppsUseLightTheme is the apps' one). Missing = dark.</summary>
public sealed class RegistryTaskbarTheme : ITaskbarTheme
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public bool IsLight
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
    }
}
