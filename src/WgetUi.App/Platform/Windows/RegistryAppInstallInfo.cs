using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace WgetUi.App.Platform.Windows;

/// <summary>
/// Reads <c>InstallLocation</c> from the uninstall entry Inno Setup writes for the per-user install (the key
/// name is the script's AppId plus "_is1"). Read once, never written.
/// </summary>
public sealed partial class RegistryAppInstallInfo : IAppInstallInfo
{
    /// <summary>HKCU key of the install. The GUID is installer/wgetui.iss's AppId (never changes).</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{45F298C5-1686-4641-BB2D-6146D39E7853}_is1";

    private readonly Lazy<string?> _location;
    private readonly ILogger<RegistryAppInstallInfo> _logger;

    public RegistryAppInstallInfo(ILogger<RegistryAppInstallInfo> logger)
    {
        _logger = logger;
        _location = new Lazy<string?>(Read);
    }

    public string? InstallLocation => _location.Value;

    private string? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the install location from the uninstall entry")]
    private partial void LogReadFailed(Exception ex);
}
