namespace WgetUi.Managers.WinGet;

/// <summary>
/// The MSIX package families installed for the current user that Windows does not let the user uninstall
/// (Settings > Installed apps hides them). Read-only, non-elevated.
/// </summary>
internal interface INonRemovablePackages
{
    /// <summary>Family names (case-insensitive set); empty when the OS query fails, so nothing is hidden.</summary>
    IReadOnlySet<string> GetNonRemovableFamilyNames();
}
