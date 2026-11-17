namespace WgetUi.Core.Models;

/// <summary>
/// Per-operation choices made by the user. Agreements are accepted only after the user saw and accepted
/// them, or enabled automatic acceptance; managers must never accept on their own.
/// </summary>
public sealed record OperationOptions(bool AcceptPackageAgreements, bool AcceptSourceAgreements)
{
    public static OperationOptions None { get; } = new(false, false);

    public static OperationOptions AcceptAll { get; } = new(true, true);
}
