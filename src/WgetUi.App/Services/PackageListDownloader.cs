using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.Core.Models;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.Services;

/// <summary>Downloads a package list from a URL (implemented over HttpClient; tests use a fake handler).</summary>
public interface IPackageListDownloader
{
    /// <exception cref="PackageListException">Not downloadable (<see cref="ErrorCodes.ListDownloadFailed"/>), too large or not a list.</exception>
    Task<PackageList> DownloadAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// GET of an http/https URL with the app's shared HttpClient: 30 s for the whole download, at most
/// <see cref="PackageListFile.MaxBytes"/> read (the body is a stream, so a huge or endless response stops at the cap).
/// Read-only: nothing is installed from here, the import popup shows every package first.
/// </summary>
public sealed partial class HttpPackageListDownloader : IPackageListDownloader
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly ProductInfoHeaderValue UserAgent = new("wgetui", AppVersion.Text);
    private readonly HttpClient _http;
    private readonly ILogger<HttpPackageListDownloader> _logger;

    public HttpPackageListDownloader(HttpClient http, ILogger<HttpPackageListDownloader> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>An absolute http:// or https:// address with a host (surrounding spaces allowed).</summary>
    public static bool TryParseUrl(string? text, out Uri? url)
    {
        url = Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && !string.IsNullOrEmpty(uri.Host)
                ? uri
                : null;
        return url is not null;
    }

    /// <summary>"example.com/lists/home.wgetui": host plus the file name, for the popup subtitle.</summary>
    public static string Label(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var file = url.Segments.Length > 1 ? Uri.UnescapeDataString(url.Segments[^1]).Trim('/') : string.Empty;
        return file.Length == 0 ? url.Host : $"{url.Host}/{file}";
    }

    public async Task<PackageList> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new PackageListException(ErrorCodes.ListDownloadFailed, $"Downloading the package list failed: HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > PackageListFile.MaxBytes)
            {
                throw new PackageListException(ErrorCodes.ListTooLarge, $"The package list is {response.Content.Headers.ContentLength} bytes.");
            }

            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var list = await PackageListFile.ReadAsync(stream, timeout.Token).ConfigureAwait(false);
                LogDownloaded(url.Host, list.Packages.Count);
                return list;
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PackageListException(ErrorCodes.ListDownloadFailed, $"Downloading the package list timed out after {Timeout.TotalSeconds} s.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new PackageListException(ErrorCodes.ListDownloadFailed, $"Downloading the package list failed: {ex.GetType().Name}.", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded a package list from {Host}: {Count} package(s)")]
    private partial void LogDownloaded(string host, int count);
}
