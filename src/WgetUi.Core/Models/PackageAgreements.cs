namespace WgetUi.Core.Models;

/// <summary>What installing <see cref="Package"/> asks the user to accept.</summary>
/// <param name="Package">The package.</param>
/// <param name="PackageTerms">Agreements of the package itself (e.g. Store license terms).</param>
/// <param name="SourceTerms">Agreements of the source it comes from (e.g. msstore terms of transaction).</param>
/// <param name="License">License name, informational only (not something to accept).</param>
/// <param name="LicenseUrl">License link, informational only.</param>
public sealed record PackageAgreements(
    Package Package,
    IReadOnlyList<Agreement> PackageTerms,
    IReadOnlyList<Agreement> SourceTerms,
    string? License = null,
    string? LicenseUrl = null)
{
    /// <summary>True when installing requires accepting package or source agreements.</summary>
    public bool RequiresAcceptance => PackageTerms.Count > 0 || SourceTerms.Count > 0;

    public static PackageAgreements None(Package package) => new(package, [], []);
}
