namespace WgetUi.App.Services.AppUpdates;

/// <summary>The one HttpClient of the self-update (release check and setup download), shared for the whole run.</summary>
public static class AppUpdateHttp
{
    /// <summary>
    /// Covers the time to the response headers (both requests read the body as a stream); the release check also
    /// applies its own 30 s limit. Redirects (GitHub serves assets from another host) are followed.
    /// </summary>
    public static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(60);

    public static HttpClient CreateClient() => new() { Timeout = HeadersTimeout };
}
