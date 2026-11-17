namespace WgetUi.Core.Models;

/// <summary>A source (catalog, repository) exposed by a manager.</summary>
/// <param name="ManagerId">Id of the owning manager.</param>
/// <param name="Id">Source id, unique within the manager (e.g. "winget", "msstore").</param>
/// <param name="DisplayName">Name shown in the UI (not translated: it is a product name).</param>
public sealed record PackageSource(string ManagerId, string Id, string DisplayName)
{
    /// <summary>
    /// Source id of installed packages the manager knows but no catalog offers (local installs, ARP entries,
    /// MSIX). Never in <see cref="Managers.IPackageManager.Sources"/>: nothing can be searched there. The UI gives
    /// it a translated name and its own badge.
    /// </summary>
    public const string LocalId = "local";
}
