namespace WgetUi.Core.Models;

/// <summary>Outcome of an install or upgrade.</summary>
/// <param name="Succeeded">True when the package was installed or upgraded.</param>
/// <param name="ErrorCode">One of <see cref="ErrorCodes"/> when it failed.</param>
/// <param name="Message">English detail for logs, never shown as UI text.</param>
/// <param name="RebootRequired">True when the installer asked for a reboot.</param>
public sealed record OperationResult(bool Succeeded, string? ErrorCode, string? Message, bool RebootRequired = false)
{
    public static OperationResult Success(bool rebootRequired = false) => new(true, null, null, rebootRequired);

    public static OperationResult Failure(string errorCode, string message) => new(false, errorCode, message);

    public static OperationResult Cancelled() => new(false, ErrorCodes.Cancelled, "Operation cancelled.");
}
