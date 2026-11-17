namespace WgetUi.Core.Models;

/// <summary>
/// Per-operation choices made by the user. Agreements are accepted only after the user saw and accepted
/// them, or enabled automatic acceptance; managers must never accept on their own.
/// </summary>
/// <param name="AcceptPackageAgreements">The user accepted the package agreements.</param>
/// <param name="AcceptSourceAgreements">The user accepted the source agreements.</param>
/// <param name="TargetVersion">Install this exact version instead of the default one (installs only; needs
/// <see cref="Managers.ManagerCapabilities.InstallVersion"/>). Null = the manager's default.</param>
public sealed record OperationOptions(bool AcceptPackageAgreements, bool AcceptSourceAgreements, string? TargetVersion = null)
{
    public static OperationOptions None { get; } = new(false, false);

    public static OperationOptions AcceptAll { get; } = new(true, true);
}
