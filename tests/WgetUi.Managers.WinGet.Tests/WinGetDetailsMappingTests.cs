using Microsoft.Management.Deployment;
using WgetUi.Core.Models;

namespace WgetUi.Managers.WinGet.Tests;

public class WinGetDetailsMappingTests
{
    private static readonly Package SevenZip = new("winget", "winget", "7zip.7zip", "7-Zip", "26.05", "26.06");

    // Values as read from the live COM API (smoke run).
    private static WinGetDetailsData SevenZipData() => new(
        "A file archiver with a high compression ratio.",
        "Free and open source file archiver",
        "Igor Pavlov",
        "https://7-zip.org/download.html",
        "https://7-zip.org/",
        "LGPL-2.1",
        "https://7-zip.org/license.txt",
        "https://github.com/ip7z/7zip/releases/tag/26.06",
        "26.05",
        "26.06",
        ["26.06", "26.05", "26.04"]);

    [Fact]
    public void DetailsCarryMetadataAndVersionsInWingetOrder()
    {
        var details = WinGetMapping.ToDetails(SevenZip, SevenZipData());

        Assert.Equal("A file archiver with a high compression ratio.", details.Description);
        Assert.Equal("Igor Pavlov", details.Publisher);
        Assert.Equal("https://7-zip.org/download.html", details.HomepageUrl);
        Assert.Equal(("LGPL-2.1", "https://7-zip.org/license.txt"), (details.License, details.LicenseUrl));
        Assert.Equal("https://github.com/ip7z/7zip/releases/tag/26.06", details.ReleaseNotesUrl);
        Assert.Equal(("26.05", "26.06"), (details.InstalledVersion, details.AvailableVersion));
        Assert.Equal(["26.06", "26.05", "26.04"], details.Versions);
    }

    [Fact]
    public void MissingFieldsFallBackOrStayNull()
    {
        // msstore: store deep link as license, http homepage, no versions; local packages: publisher only.
        var data = new WinGetDetailsData(" ", "Short one", "WhatsApp Inc.", null, "http://whatsapp.com/", "ms-windows-store://pdp/?ProductId=9NKSQGP7F2NH", "ms-windows-store://x", "not a url", "Unknown", null, ["Unknown", "", "1.0", "1.0"]);

        var details = WinGetMapping.ToDetails(SevenZip, data);

        Assert.Equal("Short one", details.Description);
        Assert.Equal("http://whatsapp.com/", details.HomepageUrl);
        Assert.Null(details.License);
        Assert.Null(details.LicenseUrl);
        Assert.Null(details.ReleaseNotesUrl);
        Assert.Null(details.InstalledVersion);
        Assert.Null(details.AvailableVersion);
        Assert.Equal(["1.0"], details.Versions);
    }

    [Theory]
    [InlineData("winget", "winget")]
    [InlineData("msstore", "msstore")]
    [InlineData(null, "local")]
    [InlineData("winget-font", "local")]
    public void InstalledPackagesWithoutAnExposedSourceAreLocal(string? catalog, string expectedSource)
    {
        var package = WinGetMapping.ToInstalled(new WinGetPackageData(@"ARP\Machine\X64\Example", "Example", catalog, "18.1", null, false));

        Assert.Equal(expectedSource, package?.SourceId);
        Assert.Equal("18.1", package?.Version);
        Assert.Null(package?.AvailableVersion);
    }

    [Fact]
    public void InstalledPackagesCarryTheirUpdateAndUnknownVersionsAsNull()
    {
        Assert.Equal("26.06", WinGetMapping.ToInstalled(new WinGetPackageData("7zip.7zip", "7-Zip", "winget", "26.05", "26.06", true))?.AvailableVersion);
        Assert.Null(WinGetMapping.ToInstalled(new WinGetPackageData("7zip.7zip", "7-Zip", "winget", "26.06", "26.06", false))?.AvailableVersion);
        Assert.Null(WinGetMapping.ToInstalled(new WinGetPackageData("Steam App 244850", "Game", null, "Unknown", null, false))?.Version);
        Assert.Null(WinGetMapping.ToInstalled(new WinGetPackageData(" ", "Nameless", null, "1", null, false)));
    }

    [Fact]
    public void TargetVersionIsFoundIgnoringCaseAndSpaces()
    {
        Assert.Equal(1, WinGetMapping.IndexOfVersion(["26.06", "26.05 ", null], "26.05"));
        Assert.Equal(-1, WinGetMapping.IndexOfVersion(["26.06"], "1.0"));
    }

    [Theory]
    [InlineData(UninstallResultStatus.Ok, null)]
    [InlineData(UninstallResultStatus.UninstallError, ErrorCodes.UninstallFailed)]
    [InlineData(UninstallResultStatus.BlockedByPolicy, ErrorCodes.BlockedByPolicy)]
    [InlineData(UninstallResultStatus.CatalogError, ErrorCodes.CatalogError)]
    [InlineData(UninstallResultStatus.InternalError, ErrorCodes.InternalError)]
    public void UninstallStatusesMapToStableCodes(UninstallResultStatus status, string? code)
    {
        var result = WinGetMapping.ToUninstallResult(status, 0, 1603, rebootRequired: false, "uninstall x");

        Assert.Equal(code is null, result.Succeeded);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public void UninstallProgressMapsToStages()
    {
        Assert.Equal(new OperationProgress(OperationStage.Uninstalling, null), WinGetMapping.ToUninstallProgress(PackageUninstallProgressState.Uninstalling, 0));
        Assert.Equal(new OperationProgress(OperationStage.Uninstalling, 50), WinGetMapping.ToUninstallProgress(PackageUninstallProgressState.Uninstalling, 0.5));
        Assert.Equal(new OperationProgress(OperationStage.Finalizing, 100), WinGetMapping.ToUninstallProgress(PackageUninstallProgressState.Finished, 1));
    }
}
