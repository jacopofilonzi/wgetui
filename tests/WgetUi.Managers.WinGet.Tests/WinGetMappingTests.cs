using Microsoft.Management.Deployment;
using WgetUi.Core.Models;

namespace WgetUi.Managers.WinGet.Tests;

public class WinGetMappingTests
{
    // Values as read from the live COM API (smoke run): installed 26.05, default install version 26.06.
    private static WinGetPackageData SevenZip(string? installed = "26.05", string? available = "26.06", string? source = "winget", bool update = true) =>
        new("7zip.7zip", "7-Zip", source, installed, available, update);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("Unknown", true)]
    [InlineData("unknown", true)]
    [InlineData("26.05", false)]
    public void UnknownVersionDetection(string? version, bool expected) =>
        Assert.Equal(expected, WinGetMapping.IsUnknownVersion(version));

    [Fact]
    public void UpgradeMapsInstalledAndAvailableVersions()
    {
        var package = WinGetMapping.ToUpgrade(SevenZip(), includeUnknown: false);

        Assert.Equal(new Package("winget", "winget", "7zip.7zip", "7-Zip", "26.05", "26.06"), package);
    }

    [Fact]
    public void UpgradeWithUnknownInstalledVersionIsHiddenUnlessRequested()
    {
        var data = SevenZip(installed: "Unknown");

        Assert.Null(WinGetMapping.ToUpgrade(data, includeUnknown: false));
        var shown = WinGetMapping.ToUpgrade(data, includeUnknown: true);
        Assert.NotNull(shown);
        Assert.Null(shown.Version);
        Assert.True(shown.IsVersionUnknown);
    }

    [Theory]
    [InlineData(false, "26.06", "winget")]
    [InlineData(true, "Unknown", "winget")]
    [InlineData(true, "26.06", "winget-font")]
    [InlineData(true, "26.06", null)]
    public void UpgradeIsSkippedWhenNotApplicable(bool update, string available, string? source) =>
        Assert.Null(WinGetMapping.ToUpgrade(SevenZip(available: available, source: source, update: update), includeUnknown: true));

    [Fact]
    public void MsStoreSourceIsRecognised() =>
        Assert.Equal("msstore", WinGetMapping.ToUpgrade(SevenZip(source: "msstore"), includeUnknown: false)?.SourceId);

    [Fact]
    public void SearchResultUsesTheSearchedSourceAndFallsBackToIdForName()
    {
        var package = WinGetMapping.ToSearchResult(new WinGetPackageData("9NBLGGH4NNS1", "", null, null, "Unknown", false), "msstore");

        Assert.NotNull(package);
        Assert.Equal(("msstore", "9NBLGGH4NNS1", "9NBLGGH4NNS1"), (package.SourceId, package.Id, package.Name));
        Assert.Null(package.Version);
    }

    [Fact]
    public void DistinctKeepsFirstOfEachKey()
    {
        var a = new Package("winget", "winget", "A", "first", "1");
        var aAgain = a with { Name = "second" };
        var otherSource = a with { SourceId = "msstore" };

        Assert.Equal([a, otherSource], WinGetMapping.Distinct([a, aAgain, otherSource]));
    }

    [Theory]
    [InlineData(PackageInstallProgressState.Queued, 0, 0, OperationStage.Preparing, null)]
    [InlineData(PackageInstallProgressState.Downloading, 0.425, 0, OperationStage.Downloading, 42.5)]
    [InlineData(PackageInstallProgressState.Downloading, 1.5, 0, OperationStage.Downloading, 100.0)]
    [InlineData(PackageInstallProgressState.Installing, 1, 0, OperationStage.Installing, null)]
    [InlineData(PackageInstallProgressState.Installing, 1, 0.5, OperationStage.Installing, 50.0)]
    [InlineData(PackageInstallProgressState.PostInstall, 1, 1, OperationStage.Finalizing, null)]
    [InlineData(PackageInstallProgressState.Finished, 1, 1, OperationStage.Finalizing, 100.0)]
    public void ProgressMapping(PackageInstallProgressState state, double download, double install, OperationStage stage, double? percent) =>
        Assert.Equal(new OperationProgress(stage, percent), WinGetMapping.ToProgress(state, download, install));

    [Fact]
    public void SuccessfulResultKeepsRebootFlag()
    {
        var result = WinGetMapping.ToResult(InstallResultStatus.Ok, 0, 0, rebootRequired: true, "install x");

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
    }

    [Theory]
    [InlineData(InstallResultStatus.InstallError, ErrorCodes.InstallFailed)]
    [InlineData(InstallResultStatus.DownloadError, ErrorCodes.DownloadFailed)]
    [InlineData(InstallResultStatus.BlockedByPolicy, ErrorCodes.BlockedByPolicy)]
    [InlineData(InstallResultStatus.NoApplicableInstallers, ErrorCodes.NoApplicableInstaller)]
    [InlineData(InstallResultStatus.NoApplicableUpgrade, ErrorCodes.NoApplicableUpgrade)]
    [InlineData(InstallResultStatus.PackageAgreementsNotAccepted, ErrorCodes.AgreementsNotAccepted)]
    [InlineData(InstallResultStatus.InternalError, ErrorCodes.InternalError)]
    public void FailedResultsMapToStableCodes(InstallResultStatus status, string code)
    {
        var result = WinGetMapping.ToResult(status, unchecked((int)0x8A150011), 1603, rebootRequired: false, "install x");

        Assert.False(result.Succeeded);
        Assert.Equal(code, result.ErrorCode);
        Assert.Contains("0x8A150011", result.Message, StringComparison.Ordinal);
        Assert.Contains("1603", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Store License Terms", null, "https://aka.ms/microsoft-store-license", "Store License Terms")]
    [InlineData("  Category ", "Social", "", "Category")]
    [InlineData(null, null, "https://example.test/terms", "https://example.test/terms")]
    public void AgreementsAreCleanedUp(string? label, string? text, string? url, string expectedLabel)
    {
        var agreement = WinGetMapping.ToAgreement(label, text, url);

        Assert.NotNull(agreement);
        Assert.Equal(expectedLabel, agreement.Label);
        Assert.Equal(string.IsNullOrWhiteSpace(url) ? null : url, agreement.Url);
    }

    [Fact]
    public void EmptyAgreementIsDropped() => Assert.Null(WinGetMapping.ToAgreement(" ", null, ""));
}
