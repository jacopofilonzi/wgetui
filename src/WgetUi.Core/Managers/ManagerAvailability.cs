namespace WgetUi.Core.Managers;

/// <summary>Result of initializing a manager.</summary>
/// <param name="IsAvailable">True when the manager can be used.</param>
/// <param name="ErrorCode">Why it is unavailable (one of <see cref="Models.ErrorCodes"/>).</param>
/// <param name="Message">English detail for logs.</param>
public sealed record ManagerAvailability(bool IsAvailable, string? ErrorCode = null, string? Message = null)
{
    public static ManagerAvailability Available { get; } = new(true);

    public static ManagerAvailability Unavailable(string errorCode, string message) => new(false, errorCode, message);
}
