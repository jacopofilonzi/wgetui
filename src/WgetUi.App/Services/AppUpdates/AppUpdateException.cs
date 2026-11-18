using WgetUi.Core.Models;

namespace WgetUi.App.Services.AppUpdates;

/// <summary>A failure of the self-update (check, download, launch) with a stable <see cref="ErrorCodes"/> code.</summary>
public sealed class AppUpdateException : Exception
{
    public AppUpdateException()
        : this(ErrorCodes.Unexpected, "Unexpected self-update failure")
    {
    }

    public AppUpdateException(string message)
        : this(ErrorCodes.Unexpected, message)
    {
    }

    public AppUpdateException(string message, Exception innerException)
        : this(ErrorCodes.Unexpected, message, innerException)
    {
    }

    public AppUpdateException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>Stable code (see <see cref="ErrorCodes"/>); the UI shows its own text for it.</summary>
    public string ErrorCode { get; }
}
