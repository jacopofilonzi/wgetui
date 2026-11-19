using System.Globalization;
using System.Text;
using System.Text.Json;
using WgetUi.Core.Models;

namespace WgetUi.Core.PackageLists;

/// <summary>
/// Reads and writes package lists. Pure: streams and strings only, no file or network access.
/// </summary>
/// <remarks>
/// wgetui's format (<c>.wgetui</c>, UTF-8 JSON):
/// <code>
/// { "format": "wgetui-packages", "formatVersion": 1, "exportedAt": "2026-11-18T21:00:00Z", "exportedBy": "wgetui 0.4.0",
///   "packages": [ { "managerId": "winget", "sourceId": "winget", "id": "7zip.7zip", "name": "7-Zip", "version": "26.00" } ] }
/// </code>
/// Unknown fields are ignored (a newer wgetui may add some); a <c>formatVersion</c> above
/// <see cref="FormatVersion"/> is <see cref="ErrorCodes.ListNewerFormat"/>. <c>managerId</c>, <c>sourceId</c> and
/// <c>id</c> are required; <c>name</c> (defaults to the id) and <c>version</c> are optional.
/// The reader also accepts the JSON of <c>winget export</c> (https://aka.ms/winget-packages.schema.2.0.json):
/// <c>Sources[].SourceDetails.Name</c> becomes the source id under manager "winget",
/// <c>Sources[].Packages[].PackageIdentifier</c> the id (and the name, until a manager reports a better one),
/// <c>Version</c> the pinned version, <c>CreationDate</c> the export date.
/// Anything else is <see cref="ErrorCodes.ListInvalid"/>; more than <see cref="MaxBytes"/> is
/// <see cref="ErrorCodes.ListTooLarge"/>. Repeated packages are kept once.
/// </remarks>
public static class PackageListFile
{
    public const string FormatName = "wgetui-packages";
    public const int FormatVersion = 1;

    /// <summary>File extension of wgetui's lists (also the installer's file association).</summary>
    public const string Extension = ".wgetui";

    /// <summary>The largest list read (file or download): far above any real list, small enough to keep in memory.</summary>
    public const int MaxBytes = 1024 * 1024;

    private const string WinGetManagerId = "winget";

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Reads at most <see cref="MaxBytes"/> from the stream and parses them.</summary>
    /// <exception cref="PackageListException">Too large or not a package list.</exception>
    public static async Task<PackageList> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new PackageListException(ErrorCodes.ListTooLarge, $"The package list is larger than {MaxBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
    }

    /// <summary>Parses a list from UTF-8 bytes (a byte order mark is allowed).</summary>
    /// <exception cref="PackageListException">Too large or not a package list.</exception>
    public static PackageList Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > MaxBytes)
        {
            throw new PackageListException(ErrorCodes.ListTooLarge, $"The package list is larger than {MaxBytes} bytes.");
        }

        if (utf8.Span.StartsWith(Encoding.UTF8.Preamble))
        {
            utf8 = utf8[Encoding.UTF8.Preamble.Length..];
        }

        try
        {
            using var document = JsonDocument.Parse(utf8, ReadOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("the root is not an object");
            }

            if (root.TryGetProperty("format", out var format))
            {
                return format.ValueKind == JsonValueKind.String && format.GetString() == FormatName
                    ? ParseWgetui(root)
                    : throw Invalid("unknown \"format\"");
            }

            if (root.TryGetProperty("Sources", out var sources))
            {
                return ParseWinGetExport(root, sources);
            }

            throw Invalid("neither a wgetui list nor a winget export");
        }
        catch (JsonException ex)
        {
            throw new PackageListException(ErrorCodes.ListInvalid, $"The package list is not valid JSON (line {ex.LineNumber}).", ex);
        }
    }

    /// <summary>Parses a list from text.</summary>
    public static PackageList Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Writes a <c>.wgetui</c> list (UTF-8, no byte order mark, indented).</summary>
    public static void Write(Stream stream, IEnumerable<PackageListEntry> packages, DateTimeOffset exportedAt, string exportedBy)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(packages);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", FormatName);
        writer.WriteNumber("formatVersion", FormatVersion);
        writer.WriteString("exportedAt", exportedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        writer.WriteString("exportedBy", exportedBy);
        writer.WriteStartArray("packages");
        foreach (var package in packages)
        {
            writer.WriteStartObject();
            writer.WriteString("managerId", package.ManagerId);
            writer.WriteString("sourceId", package.SourceId);
            writer.WriteString("id", package.Id);
            writer.WriteString("name", package.Name);
            if (package.Version is not null)
            {
                writer.WriteString("version", package.Version);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static PackageList ParseWgetui(JsonElement root)
    {
        if (!root.TryGetProperty("formatVersion", out var versionElement) || versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version) || version < 1)
        {
            throw Invalid("missing or invalid \"formatVersion\"");
        }

        if (version > FormatVersion)
        {
            throw new PackageListException(ErrorCodes.ListNewerFormat, $"The package list has format version {version}; this wgetui reads up to {FormatVersion}.");
        }

        if (!root.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("missing \"packages\" array");
        }

        var entries = new List<PackageListEntry>();
        var index = 0;
        foreach (var item in packages.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"package {index} is not an object");
            }

            var id = Required(item, "id", index);
            entries.Add(new PackageListEntry(
                Required(item, "managerId", index),
                Required(item, "sourceId", index),
                id,
                Optional(item, "name") ?? id,
                Optional(item, "version")));
            index++;
        }

        return new PackageList(Distinct(entries), Date(root, "exportedAt"), Optional(root, "exportedBy"), PackageListFormat.Wgetui);
    }

    private static PackageList ParseWinGetExport(JsonElement root, JsonElement sources)
    {
        if (sources.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("\"Sources\" is not an array");
        }

        var entries = new List<PackageListEntry>();
        var sourceIndex = 0;
        foreach (var source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object
                || !source.TryGetProperty("SourceDetails", out var details)
                || details.ValueKind != JsonValueKind.Object
                || Optional(details, "Name") is not { } sourceName)
            {
                throw Invalid($"source {sourceIndex} has no SourceDetails.Name");
            }

            if (source.TryGetProperty("Packages", out var packages))
            {
                if (packages.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid($"source {sourceIndex} Packages is not an array");
                }

                var index = 0;
                foreach (var item in packages.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        throw Invalid($"package {index} of source {sourceIndex} is not an object");
                    }

                    var id = Required(item, "PackageIdentifier", index);
                    entries.Add(new PackageListEntry(WinGetManagerId, sourceName, id, id, Optional(item, "Version")));
                    index++;
                }
            }

            sourceIndex++;
        }

        var winget = Optional(root, "WinGetVersion");
        return new PackageList(Distinct(entries), Date(root, "CreationDate"), winget is null ? "winget" : $"winget {winget}", PackageListFormat.WinGetExport);
    }

    private static string Required(JsonElement item, string name, int index) =>
        Optional(item, name) ?? throw Invalid($"package {index} has no \"{name}\"");

    private static string? Optional(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    // A date the file gets wrong is only shown, never needed: ignore it instead of rejecting the list.
    private static DateTimeOffset? Date(JsonElement root, string name) =>
        Optional(root, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static List<PackageListEntry> Distinct(List<PackageListEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. entries.Where(e => seen.Add(e.Key))];
    }

    private static PackageListException Invalid(string detail) => new(ErrorCodes.ListInvalid, $"Not a valid package list: {detail}.");
}
