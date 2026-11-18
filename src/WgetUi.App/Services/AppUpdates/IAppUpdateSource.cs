namespace WgetUi.App.Services.AppUpdates;

/// <summary>
/// Where wgetui looks for its own newer versions (today GitHub Releases; swappable, e.g. for winget, without
/// touching the UI). Read-only.
/// </summary>
public interface IAppUpdateSource
{
    /// <summary>
    /// The newest installable release, whatever the running version (the caller compares), or null when
    /// there is none. Failures throw <see cref="AppUpdateException"/>.
    /// </summary>
    Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken);
}
