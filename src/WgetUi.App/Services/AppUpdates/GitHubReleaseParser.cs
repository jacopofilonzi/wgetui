using System.Text.Json;
using System.Text.RegularExpressions;
using WgetUi.Core.Models;

namespace WgetUi.App.Services.AppUpdates;

/// <summary>What one GitHub releases response offers: the newest installable release, and why others were left out.</summary>
/// <param name="Latest">Highest installable version, or null.</param>
/// <param name="Skipped">English reasons for releases that look like wgetui versions but can't be installed (logs only).</param>
public sealed record GitHubReleaseScan(AppRelease? Latest, IReadOnlyList<string> Skipped);

/// <summary>
/// Pure reading of the GitHub REST "list releases" response (an array of release objects).
/// </summary>
/// <remarks>
/// A release is installable when it is not a draft (pre-releases count too: releases up to 0.2.x were published
/// as pre-releases, later ones are normal releases), its tag is <c>vX.Y.Z</c>, it has the asset <c>wgetui-setup-X.Y.Z-x64.exe</c> in state "uploaded"
/// with an https download URL, and a SHA-256 is known: the asset's <c>digest</c> ("sha256:&lt;hex&gt;", computed
/// by GitHub) or else the "SHA256: `&lt;hex&gt;`" line the release workflow writes in the notes. Without a
/// hash the release is skipped: the download could not be verified. The highest installable version wins,
/// whatever the order of the array. Unexpected shapes are skipped, never thrown on; only a response that is not
/// JSON (or not an array) fails, with <see cref="ErrorCodes.AppUpdateCheckFailed"/>.
/// </remarks>
public static partial class GitHubReleaseParser
{
    public static GitHubReleaseScan Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateCheckFailed, "The releases response is not valid JSON", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new AppUpdateException(ErrorCodes.AppUpdateCheckFailed, $"The releases response is a JSON {document.RootElement.ValueKind}, not an array");
            }

            AppRelease? latest = null;
            var skipped = new List<string>();
            foreach (var release in document.RootElement.EnumerateArray())
            {
                var candidate = Read(release, skipped);
                if (candidate is not null && (latest is null || candidate.Version > latest.Version))
                {
                    latest = candidate;
                }
            }

            return new GitHubReleaseScan(latest, skipped);
        }
    }

    /// <summary>The setup file name the release workflow attaches for a version ("x.y.z").</summary>
    public static string SetupAssetName(string version) => $"wgetui-setup-{version}-x64.exe";

    private static AppRelease? Read(JsonElement release, List<string> skipped)
    {
        if (release.ValueKind != JsonValueKind.Object || Bool(release, "draft") != false)
        {
            return null;
        }

        var tag = Str(release, "tag_name");
        var match = tag is null ? null : VersionTag().Match(tag);
        if (tag is null || match is null || !match.Success || !Version.TryParse(match.Groups[1].Value, out var version))
        {
            return null;
        }

        var versionText = match.Groups[1].Value;
        var assetName = SetupAssetName(versionText);
        var asset = FindAsset(release, assetName);
        if (asset is not { } found)
        {
            skipped.Add($"{tag}: no uploaded {assetName} asset");
            return null;
        }

        if (!Uri.TryCreate(Str(found, "browser_download_url"), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            skipped.Add($"{tag}: {assetName} has no https download URL");
            return null;
        }

        var sha256 = DigestHash(Str(found, "digest")) ?? BodyHash(Str(release, "body"));
        if (sha256 is null)
        {
            skipped.Add($"{tag}: no SHA-256 for {assetName} (asset digest or release notes)");
            return null;
        }

        var size = found.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes) && bytes > 0 ? bytes : 0;
        return new AppRelease(version, tag, assetName, url, size, sha256);
    }

    private static JsonElement? FindAsset(JsonElement release, string name)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object
                && string.Equals(Str(asset, "name"), name, StringComparison.Ordinal)
                && Str(asset, "state") is null or "uploaded")
            {
                return asset;
            }
        }

        return null;
    }

    private static string? DigestHash(string? digest)
    {
        const string Prefix = "sha256:";
        if (digest is null || !digest.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = digest[Prefix.Length..];
        return Sha256Hex().IsMatch(hex) ? hex.ToLowerInvariant() : null;
    }

    private static string? BodyHash(string? body)
    {
        var match = body is null ? null : BodySha256().Match(body);
        return match is { Success: true } ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    [GeneratedRegex(@"^v(\d{1,9}\.\d{1,9}\.\d{1,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionTag();

    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Hex();

    // The release workflow writes "SHA256: `<hex>`" on its own line.
    [GeneratedRegex(@"^\s*SHA-?256:\s*`?([0-9a-fA-F]{64})`?\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BodySha256();
}
