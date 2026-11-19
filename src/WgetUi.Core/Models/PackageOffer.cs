namespace WgetUi.Core.Models;

/// <summary>
/// What a source offers for one package id (see <see cref="Managers.IPackageManager.FindInSourceAsync"/>). Read-only data.
/// </summary>
/// <param name="Package">The package as the source describes it: its name, and in <see cref="Models.Package.Version"/> the
/// version an install picks by default (the latest), null when the source cannot tell.</param>
/// <param name="Versions">Versions the source offers, newest first (empty when unknown).</param>
public sealed record PackageOffer(Package Package, IReadOnlyList<string> Versions)
{
    /// <summary>True when <paramref name="version"/> is one of <see cref="Versions"/> (case-insensitive).</summary>
    public bool Offers(string? version) =>
        version is not null && Versions.Any(v => string.Equals(v, version.Trim(), StringComparison.OrdinalIgnoreCase));
}
