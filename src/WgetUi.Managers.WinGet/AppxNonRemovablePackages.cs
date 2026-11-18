using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using AppxPackageManager = Windows.Management.Deployment.PackageManager;
using AppxSignatureKind = Windows.ApplicationModel.PackageSignatureKind;

namespace WgetUi.Managers.WinGet;

/// <summary>
/// Windows' own notion of a non-removable MSIX package, the one behind <c>Get-AppxPackage</c>'s
/// <c>NonRemovable</c> and the missing Uninstall button in Settings > Installed apps.
/// </summary>
/// <remarks>
/// No public API exposes that flag (<c>Windows.ApplicationModel.Package</c> has <c>SignatureKind</c>, <c>IsFramework</c>,
/// <c>IsStub</c>… but no NonRemovable). The Appx PowerShell module computes it as
/// <c>SignatureKind == System || IsPackageFamilyInUninstallBlocklist(familyName)</c>, the latter an export of
/// <c>appxalluserstore.dll</c> (System32, callable non-elevated); this class does the same. The export is
/// undocumented, so when it is missing or fails, <see cref="KnownBlocklist"/> (the Store-signed packages found on it:
/// App Installer, i.e. winget itself, and Windows Security) stands in. Packages are read with
/// <c>PackageManager.FindPackagesForUser("")</c> (current user, no elevation). Every failure degrades to
/// "removable": hiding too little is better than hiding an app the user could uninstall.
/// </remarks>
internal sealed partial class AppxNonRemovablePackages(ILogger logger) : INonRemovablePackages
{
    /// <summary>Store-signed families on the uninstall blocklist (checked on Windows 11 26300).</summary>
    internal static readonly IReadOnlySet<string> KnownBlocklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
        "Microsoft.SecHealthUI_8wekyb3d8bbwe",
    };

    private bool _blocklistUnavailable;

    public IReadOnlySet<string> GetNonRemovableFamilyNames()
    {
        var result = new HashSet<string>(KnownBlocklist, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var package in new AppxPackageManager().FindPackagesForUser(string.Empty))
            {
                if (package.IsFramework || package.IsResourcePackage)
                {
                    continue;
                }

                var familyName = package.Id.FamilyName;
                if (package.SignatureKind == AppxSignatureKind.System || IsInUninstallBlocklist(familyName))
                {
                    result.Add(familyName);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException)
        {
            LogQueryFailed(ex);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    private bool IsInUninstallBlocklist(string familyName)
    {
        if (_blocklistUnavailable)
        {
            return false;
        }

        try
        {
            // The flag's native width is undocumented (BOOLEAN or BOOL): start from 0 and read the low byte only,
            // so either width gives the right answer and the upper bytes are never garbage.
            var present = 0;
            return NativeMethods.IsPackageFamilyInUninstallBlocklist(familyName, ref present) >= 0 && (present & 0xFF) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older or different Windows builds: rely on KnownBlocklist only.
            _blocklistUnavailable = true;
            LogBlocklistUnavailable(ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the non-removable MSIX packages failed: every package is treated as removable")]
    private partial void LogQueryFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "appxalluserstore!IsPackageFamilyInUninstallBlocklist is unavailable: using the built-in list")]
    private partial void LogBlocklistUnavailable(Exception ex);

    private static partial class NativeMethods
    {
        [LibraryImport("appxalluserstore.dll", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int IsPackageFamilyInUninstallBlocklist(string packageFamilyName, ref int isPresent);
    }
}
