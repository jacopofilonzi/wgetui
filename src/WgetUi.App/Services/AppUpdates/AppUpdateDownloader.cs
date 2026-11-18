using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using WgetUi.Core.Models;

namespace WgetUi.App.Services.AppUpdates;

/// <summary>Downloads and checks wgetui setups. Behind an interface so view-model tests never download anything.</summary>
public interface IAppUpdateDownloader
{
    /// <summary>
    /// Downloads the release's setup into the updates folder and verifies its SHA-256; returns the file path.
    /// Progress is a percentage (null = size unknown). Failures throw <see cref="AppUpdateException"/>
    /// (a hash mismatch deletes the file); cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<string> DownloadAsync(AppRelease release, IProgress<double?> progress, CancellationToken cancellationToken);

    /// <summary>True when the file is still there and still matches the release's SHA-256 (checked again before running it).</summary>
    bool Verify(string path, AppRelease release);

    /// <summary>Deletes setups left by earlier runs (best effort; a file still in use stays until next time).</summary>
    void DeleteOldSetups();
}

/// <summary>
/// Streams the setup to <c>&lt;updates folder&gt;\&lt;asset&gt;.partial</c> while hashing it, then renames it to the
/// asset name only when the SHA-256 matches, so a setup file in the folder is always a verified one. The
/// partial file is deleted on any failure or cancellation. Read-only towards the network; writes only the
/// updates folder (<see cref="Infrastructure.AppPaths.UpdatesDirectory"/>).
/// </summary>
public sealed partial class AppUpdateDownloader : IAppUpdateDownloader
{
    private const string PartialExtension = ".partial";
    private const int BufferSize = 81920;
    private readonly HttpClient _http;
    private readonly AppUpdateOptions _options;
    private readonly ILogger<AppUpdateDownloader> _logger;

    public AppUpdateDownloader(HttpClient http, AppUpdateOptions options, ILogger<AppUpdateDownloader> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<string> DownloadAsync(AppRelease release, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(progress);
        var target = Path.Combine(_options.DownloadDirectory, release.AssetName);
        var partial = target + PartialExtension;
        try
        {
            Directory.CreateDirectory(_options.DownloadDirectory);
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            request.Headers.UserAgent.Add(_options.UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AppUpdateException(ErrorCodes.AppUpdateDownloadFailed, $"Download of {release.AssetName} answered HTTP {(int)response.StatusCode}");
            }

            var total = response.Content.Headers.ContentLength ?? (release.Size > 0 ? release.Size : null);
            string actual;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (input.ConfigureAwait(false))
                {
                    var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                    await using (output.ConfigureAwait(false))
                    {
                        await CopyAsync(input, output, hash, total, progress, cancellationToken).ConfigureAwait(false);
                    }
                }

                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (!string.Equals(actual, release.Sha256, StringComparison.Ordinal))
            {
                throw new AppUpdateException(ErrorCodes.AppUpdateHashMismatch, $"{release.AssetName}: SHA-256 {actual}, expected {release.Sha256}");
            }

            File.Move(partial, target, overwrite: true);
            LogDownloaded(release.AssetName, target);
            return target;
        }
        catch (HttpRequestException ex)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateDownloadFailed, $"Download of {release.AssetName} failed", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateDownloadFailed, $"Could not write {partial}", ex);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    public bool Verify(string path, AppRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        try
        {
            using var file = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(SHA256.HashData(file));
            return string.Equals(actual, release.Sha256, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LogVerifyFailed(path, ex);
            return false;
        }
    }

    public void DeleteOldSetups()
    {
        if (!Directory.Exists(_options.DownloadDirectory))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_options.DownloadDirectory, "wgetui-setup-*"))
            {
                TryDelete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(_options.DownloadDirectory, ex);
        }
    }

    private static async Task CopyAsync(Stream input, Stream output, IncrementalHash hash, long? total, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long copied = 0;
        var lastPercent = -1;
        progress.Report(total > 0 ? 0 : null);
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            if (total > 0)
            {
                // One report per whole percent: the UI is updated through the dispatcher.
                var percent = (int)Math.Min(100, copied * 100 / total.Value);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(percent);
                }
            }
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(path, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded and verified {Asset} to {Path}")]
    private partial void LogDownloaded(string asset, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not verify {Path}")]
    private partial void LogVerifyFailed(string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Could not delete {Path} (in use?); retried at the next start")]
    private partial void LogDeleteFailed(string path, Exception ex);
}
