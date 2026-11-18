using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Services.AppUpdates;
using WgetUi.Core.Models;

namespace WgetUi.App.Tests;

/// <summary>The downloader against a scripted HTTP handler and a temp folder: never the network, never runs anything.</summary>
public sealed class AppUpdateDownloaderTests : IDisposable
{
    private const string Payload = "not really a setup";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wgetui-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static AppRelease Release(string sha256) =>
        new(new Version(0, 3, 0), "v0.3.0", "wgetui-setup-0.3.0-x64.exe", new Uri("https://example.com/wgetui-setup-0.3.0-x64.exe"), Payload.Length, sha256);

    private AppUpdateDownloader Downloader(ScriptedHandler handler) =>
        new(new HttpClient(handler), new AppUpdateOptions(new Version(0, 2, 0), @"C:\app", _dir), NullLogger<AppUpdateDownloader>.Instance);

    [Fact]
    public async Task AVerifiedDownloadLandsUnderTheAssetName()
    {
        var progress = new List<double?>();
        var release = Release(Sha(Payload));

        var path = await Downloader(new ScriptedHandler(HttpStatusCode.OK, Payload)).DownloadAsync(release, new SyncProgress(progress), TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_dir, "wgetui-setup-0.3.0-x64.exe"), path);
        Assert.Equal(Payload, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], Directory.GetFiles(_dir));
        Assert.Equal(100, progress[^1]);
        Assert.True(Downloader(new ScriptedHandler(HttpStatusCode.OK, "")).Verify(path, release));
    }

    [Fact]
    public async Task AHashMismatchLeavesNoFile()
    {
        var error = await Assert.ThrowsAsync<AppUpdateException>(() =>
            Downloader(new ScriptedHandler(HttpStatusCode.OK, Payload)).DownloadAsync(Release(new string('0', 64)), new SyncProgress([]), TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.AppUpdateHashMismatch, error.ErrorCode);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task HttpErrorsAreDownloadFailures()
    {
        var error = await Assert.ThrowsAsync<AppUpdateException>(() =>
            Downloader(new ScriptedHandler(HttpStatusCode.NotFound, "")).DownloadAsync(Release(Sha(Payload)), new SyncProgress([]), TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.AppUpdateDownloadFailed, error.ErrorCode);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task ATamperedFileFailsVerification()
    {
        var release = Release(Sha(Payload));
        var downloader = Downloader(new ScriptedHandler(HttpStatusCode.OK, Payload));
        var path = await downloader.DownloadAsync(release, new SyncProgress([]), TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(path, "changed", TestContext.Current.CancellationToken);

        Assert.False(downloader.Verify(path, release));
        Assert.False(downloader.Verify(Path.Combine(_dir, "missing.exe"), release));
    }

    [Fact]
    public void OldSetupsAndPartialsAreDeletedOtherFilesKept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "wgetui-setup-0.1.0-x64.exe"), "old");
        File.WriteAllText(Path.Combine(_dir, "wgetui-setup-0.2.0-x64.exe.partial"), "half");
        File.WriteAllText(Path.Combine(_dir, "setup.log"), "log");

        Downloader(new ScriptedHandler(HttpStatusCode.OK, "")).DeleteOldSetups(null);

        Assert.Equal([Path.Combine(_dir, "setup.log")], Directory.GetFiles(_dir));
    }

    [Fact]
    public void ThePendingSetupIsKept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "wgetui-setup-0.1.0-x64.exe"), "old");
        File.WriteAllText(Path.Combine(_dir, "wgetui-setup-0.3.0-x64.exe"), "pending");

        Downloader(new ScriptedHandler(HttpStatusCode.OK, "")).DeleteOldSetups("wgetui-setup-0.3.0-x64.exe");

        Assert.Equal([Path.Combine(_dir, "wgetui-setup-0.3.0-x64.exe")], Directory.GetFiles(_dir));
    }

    [Fact]
    public void NoFolderNothingToDelete()
    {
        Downloader(new ScriptedHandler(HttpStatusCode.OK, "")).DeleteOldSetups(null);

        Assert.False(Directory.Exists(_dir));
    }
}

/// <summary>Records progress synchronously (Progress&lt;T&gt; would post to the thread pool).</summary>
internal sealed class SyncProgress(List<double?> values) : IProgress<double?>
{
    public void Report(double? value) => values.Add(value);
}
