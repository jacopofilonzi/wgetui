using System.Text;
using WgetUi.Core.Models;
using WgetUi.Core.PackageLists;

namespace WgetUi.Core.Tests;

public sealed class PackageListFileTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string Code(Action read) => Assert.Throws<PackageListException>(read).ErrorCode;

    [Fact]
    public void ReadsAWgetuiListIgnoringUnknownFieldsAndDuplicates()
    {
        var list = PackageListFile.Parse(Fixture("sample.wgetui"));

        Assert.Equal(PackageListFormat.Wgetui, list.Format);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 21, 0, 0, TimeSpan.Zero), list.ExportedAt);
        Assert.Equal("wgetui 0.4.0", list.ExportedBy);
        Assert.Equal(
            [
                new PackageListEntry("winget", "winget", "7zip.7zip", "7-Zip", "26.00"),
                new PackageListEntry("winget", "msstore", "9NKSQGP7F2NH", "WhatsApp", null),
                new PackageListEntry("winget", "winget", "Git.Git", "Git.Git", null),
            ],
            list.Packages);
    }

    [Fact]
    public void ReadsAWingetExport()
    {
        var list = PackageListFile.Parse(Fixture("winget-export.json"));

        Assert.Equal(PackageListFormat.WinGetExport, list.Format);
        Assert.Equal("winget 1.29.380", list.ExportedBy);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 1, 10, 11, 879, TimeSpan.Zero), list.ExportedAt);
        Assert.Equal(
            [
                new PackageListEntry("winget", "winget", "7zip.7zip", "7zip.7zip", "26.06"),
                new PackageListEntry("winget", "winget", "Git.Git", "Git.Git", "2.56.0.4"),
                new PackageListEntry("winget", "winget", "VideoLAN.VLC", "VideoLAN.VLC", "3.0.24.0"),
                new PackageListEntry("winget", "winget", "WireGuard.WireGuard", "WireGuard.WireGuard", null),
                new PackageListEntry("winget", "msstore", "9NKSQGP7F2NH", "9NKSQGP7F2NH", null),
            ],
            list.Packages);
    }

    [Fact]
    public void WriteThenReadGivesTheSameList()
    {
        PackageListEntry[] entries =
        [
            new("winget", "winget", "7zip.7zip", "7-Zip", "26.00"),
            new("winget", "msstore", "9NKSQGP7F2NH", "WhatsApp «ü»", null),
        ];
        using var stream = new MemoryStream();

        PackageListFile.Write(stream, entries, new DateTimeOffset(2026, 11, 18, 23, 0, 0, TimeSpan.FromHours(2)), "wgetui 0.4.0");
        var json = Encoding.UTF8.GetString(stream.ToArray());
        var list = PackageListFile.Parse(stream.ToArray());

        Assert.Contains("\"format\": \"wgetui-packages\"", json, StringComparison.Ordinal);
        Assert.Contains("\"exportedAt\": \"2026-11-18T21:00:00Z\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"version\": null", json, StringComparison.Ordinal);
        Assert.Equal(entries, list.Packages);
        Assert.Equal("wgetui 0.4.0", list.ExportedBy);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 21, 0, 0, TimeSpan.Zero), list.ExportedAt);
    }

    [Fact]
    public void AByteOrderMarkIsAccepted()
    {
        var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Fixture("sample.wgetui")).ToArray();

        Assert.Equal(3, PackageListFile.Parse(bytes).Packages.Count);
    }

    [Fact]
    public void ANewerFormatVersionIsRefused() =>
        Assert.Equal(ErrorCodes.ListNewerFormat, Code(() => PackageListFile.Parse("""{ "format": "wgetui-packages", "formatVersion": 2, "packages": [] }""")));

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "format": "something-else", "formatVersion": 1, "packages": [] }""")]
    [InlineData("""{ "format": "wgetui-packages", "packages": [] }""")]
    [InlineData("""{ "format": "wgetui-packages", "formatVersion": "1", "packages": [] }""")]
    [InlineData("""{ "format": "wgetui-packages", "formatVersion": 1 }""")]
    [InlineData("""{ "format": "wgetui-packages", "formatVersion": 1, "packages": [ { "managerId": "winget", "sourceId": "winget" } ] }""")]
    [InlineData("""{ "format": "wgetui-packages", "formatVersion": 1, "packages": [ { "managerId": "winget", "id": "a" } ] }""")]
    [InlineData("""{ "format": "wgetui-packages", "formatVersion": 1, "packages": [ { "managerId": 3, "sourceId": "winget", "id": "a" } ] }""")]
    [InlineData("""{ "format": "wgetui-packages", "formatVersion": 1, "packages": [ "a" ] }""")]
    [InlineData("""{ "Sources": [ { "Packages": [ { "PackageIdentifier": "a" } ] } ] }""")]
    [InlineData("""{ "Sources": [ { "SourceDetails": { "Name": "winget" }, "Packages": [ { "Version": "1" } ] } ] }""")]
    [InlineData("""{ "Sources": {} }""")]
    public void AnythingElseIsInvalid(string json) => Assert.Equal(ErrorCodes.ListInvalid, Code(() => PackageListFile.Parse(json)));

    [Fact]
    public void AnEmptyListIsValid() =>
        Assert.Empty(PackageListFile.Parse("""{ "format": "wgetui-packages", "formatVersion": 1, "packages": [] }""").Packages);

    [Fact]
    public void ABadDateIsIgnored() =>
        Assert.Null(PackageListFile.Parse("""{ "format": "wgetui-packages", "formatVersion": 1, "exportedAt": "yesterday", "packages": [] }""").ExportedAt);

    [Fact]
    public async Task ReadingStopsPastTheSizeCap()
    {
        var huge = new byte[PackageListFile.MaxBytes + 1];
        Array.Fill(huge, (byte)' ');
        using var stream = new MemoryStream(huge);

        var ex = await Assert.ThrowsAsync<PackageListException>(() => PackageListFile.ReadAsync(stream, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.ListTooLarge, ex.ErrorCode);
        Assert.Equal(ErrorCodes.ListTooLarge, Code(() => PackageListFile.Parse(huge)));
    }

    [Fact]
    public async Task ReadAsyncParsesAStream()
    {
        using var stream = new MemoryStream(Fixture("winget-export.json"));

        Assert.Equal(5, (await PackageListFile.ReadAsync(stream, TestContext.Current.CancellationToken)).Packages.Count);
    }
}
