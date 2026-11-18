namespace WgetUi.App.Platform;

/// <summary>
/// Where the wgetui setup installed the app for this user (read-only). Only the copy running from there
/// updates itself; behind an interface so tests never read the real registry.
/// </summary>
public interface IAppInstallInfo
{
    /// <summary>The install folder the setup recorded, or null when wgetui was never installed (or it can't be read).</summary>
    string? InstallLocation { get; }
}
