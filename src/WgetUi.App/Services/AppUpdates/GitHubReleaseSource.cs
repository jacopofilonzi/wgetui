using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using WgetUi.Core.Models;

namespace WgetUi.App.Services.AppUpdates;

/// <summary>
/// wgetui's releases on GitHub (<c>jacopofilonzi/wgetui</c>), read through the public REST API without
/// authentication (60 requests per hour per IP: one check per hour is far below). Read-only.
/// </summary>
/// <remarks>
/// The only request header beyond the API's own is <c>User-Agent: wgetui/&lt;version&gt;</c> (GitHub requires
/// one); nothing about the user or the machine is sent. 403 with no remaining quota and 429 are reported as
/// <see cref="ErrorCodes.AppUpdateRateLimited"/>, every other failure as
/// <see cref="ErrorCodes.AppUpdateCheckFailed"/>. Selection rules are in <see cref="GitHubReleaseParser"/>.
/// </remarks>
public sealed partial class GitHubReleaseSource : IAppUpdateSource
{
    public static readonly Uri ReleasesUri = new("https://api.github.com/repos/jacopofilonzi/wgetui/releases?per_page=30");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _http;
    private readonly ProductInfoHeaderValue _userAgent;
    private readonly ILogger<GitHubReleaseSource> _logger;

    public GitHubReleaseSource(HttpClient http, AppUpdateOptions options, ILogger<GitHubReleaseSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _userAgent = options.UserAgent;
        _logger = logger;
    }

    public async Task<AppRelease?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUri);
        request.Headers.UserAgent.Add(_userAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        string json;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (IsRateLimited(response))
            {
                throw new AppUpdateException(ErrorCodes.AppUpdateRateLimited, $"GitHub rate limit reached (HTTP {(int)response.StatusCode})");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AppUpdateException(ErrorCodes.AppUpdateCheckFailed, $"GitHub answered HTTP {(int)response.StatusCode}");
            }

            json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateCheckFailed, "The releases request failed", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateCheckFailed, $"The releases request timed out after {RequestTimeout.TotalSeconds:0} s", ex);
        }

        var scan = GitHubReleaseParser.Parse(json);
        foreach (var reason in scan.Skipped)
        {
            LogSkipped(reason);
        }

        LogChecked(scan.Latest?.VersionText ?? "none");
        return scan.Latest;
    }

    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests
        || (response.StatusCode == HttpStatusCode.Forbidden
            && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
            && remaining.Contains("0", StringComparer.Ordinal));

    [LoggerMessage(Level = LogLevel.Information, Message = "Release skipped: {Reason}")]
    private partial void LogSkipped(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "wgetui releases checked: newest installable {Version}")]
    private partial void LogChecked(string version);
}
