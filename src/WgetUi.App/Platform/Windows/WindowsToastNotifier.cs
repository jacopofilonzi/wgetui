using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace WgetUi.App.Platform.Windows;

/// <summary>
/// Windows toasts from an unpackaged exe. Windows needs an AppUserModelID it knows: we register one under
/// HKCU\Software\Classes\AppUserModelId\wgetui (DisplayName + IconUri, no shortcut needed) and give the
/// process the same id. Without a COM activator a click only reaches us while the app runs
/// (<see cref="ToastNotification.Activated"/>); that is all the app needs.
/// </summary>
/// <remarks>
/// <see cref="ToastNotifier.Setting"/> throws ERROR_NOT_FOUND until the first toast after registration, so it
/// is never used as a gate. Writes HKCU (side effect, documented in AGENTS.md).
/// </remarks>
public sealed partial class WindowsToastNotifier : IToastNotifier
{
    public const string AppUserModelId = "wgetui";
    private const string RegistrationKey = @"Software\Classes\AppUserModelId\" + AppUserModelId;
    private readonly ILogger<WindowsToastNotifier> _logger;
    private readonly Lock _gate = new();
    private ToastNotifier? _notifier;

    public WindowsToastNotifier(ILogger<WindowsToastNotifier> logger) => _logger = logger;

    public event EventHandler<ToastAction>? Activated;

    /// <summary>Gives the process the app's id (taskbar grouping and toasts). Call before any window exists.</summary>
    public static void SetProcessAppUserModelId()
    {
        // Best effort: a failure only affects taskbar grouping; toasts still name the id explicitly.
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    /// <summary>Registers the id with its display name and icon (a PNG path). Safe to call at every start.</summary>
    public void Register(string? iconPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistrationKey);
            key.SetValue("DisplayName", "wgetui");
            if (iconPath is not null)
            {
                key.SetValue("IconUri", iconPath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            LogRegisterFailed(ex);
        }
    }

    public void Show(string title, string body, ToastAction action = ToastAction.OpenWindow)
    {
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text></binding></visual></toast>");
            var toast = new ToastNotification(xml);
            toast.Activated += (_, _) => Activated?.Invoke(this, action);
            toast.Failed += (_, e) => LogToastFailed(e.ErrorCode?.HResult ?? 0);
            Notifier().Show(toast);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException)
        {
            LogShowFailed(ex);
        }
    }

    private ToastNotifier Notifier()
    {
        lock (_gate)
        {
            return _notifier ??= ToastNotificationManager.CreateToastNotifier(AppUserModelId);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not register the notification app id")]
    private partial void LogRegisterFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not show a notification")]
    private partial void LogShowFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification failed with 0x{HResult:X8}")]
    private partial void LogToastFailed(int hResult);
}
