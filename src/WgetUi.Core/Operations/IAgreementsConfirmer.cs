using WgetUi.Core.Models;

namespace WgetUi.Core.Operations;

/// <summary>Shows agreements to the user (the UI implements it with a modal).</summary>
public interface IAgreementsConfirmer
{
    /// <summary>Lists every agreement at once; true only when the user explicitly accepted them.</summary>
    Task<bool> ConfirmAsync(IReadOnlyList<PackageAgreements> agreements, CancellationToken cancellationToken);
}
