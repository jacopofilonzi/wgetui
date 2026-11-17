using WgetUi.Core.Models;

namespace WgetUi.Core.Managers;

/// <summary>
/// Thrown by a manager when a query fails. Carries a stable <see cref="ErrorCode"/> (one of
/// <see cref="ErrorCodes"/>) for the UI and an English message for logs.
/// </summary>
public sealed class PackageManagerException : Exception
{
    public PackageManagerException()
        : this(ErrorCodes.Unexpected, "Package manager error.")
    {
    }

    public PackageManagerException(string message)
        : this(ErrorCodes.Unexpected, message)
    {
    }

    public PackageManagerException(string message, Exception innerException)
        : this(ErrorCodes.Unexpected, message, innerException)
    {
    }

    public PackageManagerException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException) => ErrorCode = errorCode;

    public string ErrorCode { get; }
}
