using Microsoft.Extensions.Logging;

namespace WgetUi.App.Services.Changelog;

/// <summary>wgetui's release notes, newest first. Behind an interface so tests script them.</summary>
public interface IChangelogSource
{
    IReadOnlyList<ChangelogVersion> Versions { get; }
}

/// <summary>
/// CHANGELOG.md from the repository root, embedded in the exe (WgetUi.App.csproj) and parsed on first use.
/// English only, by decision: the notes are not translated. A missing resource gives no versions (logged).
/// </summary>
public sealed partial class EmbeddedChangelogSource : IChangelogSource
{
    /// <summary>Manifest resource name set in WgetUi.App.csproj.</summary>
    public const string ResourceName = "wgetui.CHANGELOG.md";

    private readonly Lazy<IReadOnlyList<ChangelogVersion>> _versions;
    private readonly ILogger<EmbeddedChangelogSource> _logger;

    public EmbeddedChangelogSource(ILogger<EmbeddedChangelogSource> logger)
    {
        _logger = logger;
        _versions = new Lazy<IReadOnlyList<ChangelogVersion>>(Load);
    }

    public IReadOnlyList<ChangelogVersion> Versions => _versions.Value;

    /// <summary>The embedded text, or null when the resource is missing.</summary>
    public static string? ReadText()
    {
        using var stream = typeof(EmbeddedChangelogSource).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private IReadOnlyList<ChangelogVersion> Load()
    {
        var text = ReadText();
        if (text is null)
        {
            LogMissing(ResourceName);
            return [];
        }

        return ChangelogParser.Parse(text);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Release notes resource {Name} not found: What's new stays empty")]
    private partial void LogMissing(string name);
}
