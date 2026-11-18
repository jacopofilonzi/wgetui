using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WgetUi.App.Services.AppUpdates;
using WgetUi.Core.Models;

namespace WgetUi.App.Tests;

/// <summary>
/// Release selection against a fixture shaped like a real GitHub "list releases" response (anonymised), and the
/// HTTP source against a scripted handler. No test reaches the network.
/// </summary>
public sealed class GitHubReleaseTests
{
    private static readonly string Fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-releases.json"));

    [Fact]
    public void PicksTheHighestInstallableVersionPreReleasesIncluded()
    {
        var scan = GitHubReleaseParser.Parse(Fixture);

        // v0.9.0 is a draft, "nightly" is not a version, v0.5.0 has no setup of its own, v0.4.1 has no hash.
        var latest = Assert.IsType<AppRelease>(scan.Latest);
        Assert.Equal(new Version(0, 4, 0), latest.Version);
        Assert.Equal("v0.4.0", latest.Tag);
        Assert.Equal("wgetui-setup-0.4.0-x64.exe", latest.AssetName);
        Assert.Equal(new Uri("https://github.com/example-owner/wgetui/releases/download/v0.4.0/wgetui-setup-0.4.0-x64.exe"), latest.DownloadUrl);
        Assert.Equal(14336568, latest.Size);
    }

    [Fact]
    public void WithoutADigestTheHashComesFromTheNotes()
    {
        var latest = GitHubReleaseParser.Parse(Fixture).Latest!;

        Assert.Equal(string.Concat(Enumerable.Repeat("ab", 32)), latest.Sha256);
    }

    [Fact]
    public void TheAssetDigestWinsOverTheNotes()
    {
        var json = Releases(r => r["tag_name"]!.GetValue<string>() == "v0.3.0");

        var latest = GitHubReleaseParser.Parse(json).Latest!;

        Assert.Equal(new Version(0, 3, 0), latest.Version);
        Assert.Equal(new string('3', 64), latest.Sha256);
    }

    [Fact]
    public void SkippedReleasesSayWhy()
    {
        var skipped = GitHubReleaseParser.Parse(Fixture).Skipped;

        Assert.Contains(skipped, s => s.StartsWith("v0.5.0: no uploaded wgetui-setup-0.5.0-x64.exe", StringComparison.Ordinal));
        Assert.Contains(skipped, s => s.StartsWith("v0.4.1: no SHA-256", StringComparison.Ordinal));
        Assert.DoesNotContain(skipped, s => s.StartsWith("v0.9.0", StringComparison.Ordinal));
    }

    [Fact]
    public void DraftsAndIncompleteUploadsAreNeverOffered()
    {
        var onlyDraft = Releases(r => r["draft"]!.GetValue<bool>());
        Assert.Null(GitHubReleaseParser.Parse(onlyDraft).Latest);

        var starter = Releases(r => r["tag_name"]!.GetValue<string>() == "v0.2.0", r => r["assets"]![0]!["state"] = "starter");
        Assert.Null(GitHubReleaseParser.Parse(starter).Latest);
    }

    [Fact]
    public void NonHttpsDownloadsAreSkipped()
    {
        var json = Releases(
            r => r["tag_name"]!.GetValue<string>() == "v0.2.0",
            r => r["assets"]![0]!["browser_download_url"] = "http://example.com/wgetui-setup-0.2.0-x64.exe");

        Assert.Null(GitHubReleaseParser.Parse(json).Latest);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[42, \"x\", {}, {\"tag_name\": 1}]")]
    public void EmptyOrOddListsHaveNoRelease(string json) => Assert.Null(GitHubReleaseParser.Parse(json).Latest);

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"message\": \"Not Found\"}")]
    public void ResponsesThatAreNotAListFail(string json)
    {
        var error = Assert.Throws<AppUpdateException>(() => GitHubReleaseParser.Parse(json));

        Assert.Equal(ErrorCodes.AppUpdateCheckFailed, error.ErrorCode);
    }

    [Fact]
    public void InstalledCopyMeansTheInstallFolderNormalized()
    {
        var options = new AppUpdateOptions(new Version(0, 2, 0), @"C:\Users\u\AppData\Local\Programs\wgetui", @"C:\tmp");

        Assert.True(options.IsInstalledCopy(@"c:\users\U\AppData\Local\Programs\wgetui\"));
        Assert.True(options.IsInstalledCopy(@"""C:\Users\u\AppData\Local\Programs\wgetui"""));
        Assert.False(options.IsInstalledCopy(@"C:\Users\u\AppData\Local\Programs\wgetui-old"));
        Assert.False(options.IsInstalledCopy(null));
        Assert.False(options.IsInstalledCopy("  "));
    }

    [Fact]
    public async Task TheSourceSendsOnlyTheApiHeadersAndItsVersion()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, Fixture);
        var source = Source(handler);

        var latest = await source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Version(0, 4, 0), latest?.Version);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(GitHubReleaseSource.ReleasesUri, request.RequestUri);
        Assert.Equal("wgetui/0.2.0", request.Headers.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.Equal(["2022-11-28"], request.Headers.GetValues("X-GitHub-Api-Version"));
        Assert.Null(request.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, null, ErrorCodes.AppUpdateRateLimited)]
    [InlineData(HttpStatusCode.Forbidden, "0", ErrorCodes.AppUpdateRateLimited)]
    [InlineData(HttpStatusCode.Forbidden, "12", ErrorCodes.AppUpdateCheckFailed)]
    [InlineData(HttpStatusCode.InternalServerError, null, ErrorCodes.AppUpdateCheckFailed)]
    public async Task HttpFailuresHaveStableCodes(HttpStatusCode status, string? remaining, string expected)
    {
        var handler = new ScriptedHandler(status, "{}");
        if (remaining is not null)
        {
            handler.Headers["X-RateLimit-Remaining"] = remaining;
        }

        var error = await Assert.ThrowsAsync<AppUpdateException>(() => Source(handler).GetLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expected, error.ErrorCode);
    }

    [Fact]
    public async Task ANetworkErrorIsACheckFailure()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, "[]") { Failure = new HttpRequestException("offline") };

        var error = await Assert.ThrowsAsync<AppUpdateException>(() => Source(handler).GetLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.AppUpdateCheckFailed, error.ErrorCode);
    }

    private static GitHubReleaseSource Source(ScriptedHandler handler) =>
        new(new HttpClient(handler), new AppUpdateOptions(new Version(0, 2, 0), @"C:\app", @"C:\tmp"), NullLogger<GitHubReleaseSource>.Instance);

    /// <summary>The fixture reduced to the releases <paramref name="keep"/> accepts, each optionally changed.</summary>
    private static string Releases(Func<JsonNode, bool> keep, Action<JsonNode>? change = null)
    {
        var all = JsonNode.Parse(Fixture)!.AsArray();
        var kept = new JsonArray();
        foreach (var release in all.Where(r => keep(r!)).Select(r => r!.DeepClone()))
        {
            change?.Invoke(release);
            kept.Add(release);
        }

        return kept.ToJsonString();
    }
}

/// <summary>Answers every request with a fixed response (or failure) and records the requests. No network.</summary>
internal sealed class ScriptedHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public Dictionary<string, string> Headers { get; } = [];

    public Exception? Failure { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Failure is not null)
        {
            return Task.FromException<HttpResponseMessage>(Failure);
        }

        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        foreach (var (name, value) in Headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return Task.FromResult(response);
    }
}
