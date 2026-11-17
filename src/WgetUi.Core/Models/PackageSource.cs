namespace WgetUi.Core.Models;

/// <summary>A source (catalog, repository) exposed by a manager.</summary>
/// <param name="ManagerId">Id of the owning manager.</param>
/// <param name="Id">Source id, unique within the manager (e.g. "winget", "msstore").</param>
/// <param name="DisplayName">Name shown in the UI (not translated: it is a product name).</param>
public sealed record PackageSource(string ManagerId, string Id, string DisplayName);
