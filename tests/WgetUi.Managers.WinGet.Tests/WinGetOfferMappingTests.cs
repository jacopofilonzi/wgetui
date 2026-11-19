using WgetUi.Core.Models;

namespace WgetUi.Managers.WinGet.Tests;

public class WinGetOfferMappingTests
{
    private static readonly Package Requested = new("winget", "winget", "7zip.7zip", "7zip.7zip", "26.00");

    [Fact]
    public void AnOfferCarriesTheCatalogNameTheDefaultVersionAndTheVersions()
    {
        var offer = WinGetMapping.ToOffer(Requested, "7zip.7zip", "7-Zip", "26.06", ["26.06", "26.05", " 26.00 ", "26.05"]);

        Assert.Equal(new Package("winget", "winget", "7zip.7zip", "7-Zip", "26.06"), offer.Package);
        Assert.Equal(["26.06", "26.05", "26.00"], offer.Versions);
        Assert.True(offer.Offers("26.00"));
        Assert.False(offer.Offers("25.01"));
        Assert.False(offer.Offers(null));
    }

    [Fact]
    public void StoreAppsWithUnknownVersionsHaveNoLatestVersion()
    {
        var offer = WinGetMapping.ToOffer(Requested with { SourceId = "msstore", Id = "9NKSQGP7F2NH" }, "9NKSQGP7F2NH", "WhatsApp", "Unknown", ["Unknown"]);

        Assert.Null(offer.Package.Version);
        Assert.Empty(offer.Versions);
        Assert.Equal("WhatsApp", offer.Package.Name);
    }

    [Fact]
    public void BlankCatalogValuesKeepTheRequestedOnes()
    {
        var offer = WinGetMapping.ToOffer(Requested, " ", null, null, ["2.0", "1.0"]);

        Assert.Equal(("7zip.7zip", "7zip.7zip", "2.0"), (offer.Package.Id, offer.Package.Name, offer.Package.Version));
    }
}
