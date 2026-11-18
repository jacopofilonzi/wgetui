namespace WgetUi.App.Services.AppUpdates;

/// <summary>An installable wgetui release: its setup exe and the SHA-256 the download must match.</summary>
/// <param name="Version">x.y.z of the release.</param>
/// <param name="Tag">Tag it was published under (<c>vX.Y.Z</c>).</param>
/// <param name="AssetName">File name of the setup exe (<c>wgetui-setup-X.Y.Z-x64.exe</c>).</param>
/// <param name="DownloadUrl">Where the setup exe is downloaded from (https).</param>
/// <param name="Size">Size in bytes announced by the source (0 when unknown).</param>
/// <param name="Sha256">Expected SHA-256 of the setup exe, 64 lowercase hex characters.</param>
public sealed record AppRelease(Version Version, string Tag, string AssetName, Uri DownloadUrl, long Size, string Sha256)
{
    /// <summary>"x.y.z", as shown to the user.</summary>
    public string VersionText => Version.ToString(3);
}
