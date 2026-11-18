using System.Text;
using System.Text.RegularExpressions;
using WgetUi.App.Infrastructure;

namespace WgetUi.App.Services.Changelog;

/// <summary>One "### Added" (or any other) group of a version's notes; <see cref="Heading"/> is null for bullets before any heading.</summary>
public sealed record ChangelogSection(string? Heading, IReadOnlyList<string> Items)
{
    public bool HasHeading => !string.IsNullOrEmpty(Heading);
}

/// <summary>The notes of one released version.</summary>
/// <param name="Version">x.y.z.</param>
/// <param name="Date">The date written after the version (as written, usually YYYY-MM-DD), or null.</param>
/// <param name="Sections">Its groups of bullets, in file order (empty groups left out).</param>
public sealed record ChangelogVersion(Version Version, string? Date, IReadOnlyList<ChangelogSection> Sections)
{
    /// <summary>"x.y.z", as shown to the user.</summary>
    public string VersionText => Version.ToString(3);

    public bool HasDate => !string.IsNullOrEmpty(Date);
}

/// <summary>
/// Pure reading of CHANGELOG.md (Keep a Changelog style) into versions, newest first.
/// </summary>
/// <remarks>
/// Recognised lines: <c>## [x.y.z] - date</c> (brackets and date optional) starts a version; any other
/// <c>##</c> heading (e.g. "Unreleased") ends the current one and its content is skipped. <c>### Heading</c>
/// starts a section, whatever its name. <c>- item</c> / <c>* item</c> is a bullet, and the indented lines after
/// it continue it; another non-empty line inside a version becomes an item of its own. Link reference
/// definitions (<c>[x]: url</c>) and the text before the first version are ignored. Inline markdown is reduced
/// to plain text: <c>**bold**</c>, <c>`code`</c> and <c>[text](url)</c> keep only their text. A version listed
/// twice keeps its first section. Never throws on content.
/// </remarks>
public static partial class ChangelogParser
{
    public static IReadOnlyList<ChangelogVersion> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var versions = new List<ChangelogVersion>();
        Builder? current = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ', '\t');
            if (line.StartsWith("## ", StringComparison.Ordinal) || line == "##")
            {
                Flush(current, versions);
                current = VersionHeading(line);
                continue;
            }

            if (current is null || line.Length == 0 || LinkDefinition().IsMatch(line))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                current.StartSection(Inline(line[4..].Trim()));
            }
            else if (line.StartsWith('#'))
            {
                // A deeper heading: shown as a section of its own.
                current.StartSection(Inline(line.TrimStart('#').Trim()));
            }
            else if (Bullet().Match(line) is { Success: true } bullet)
            {
                current.AddItem(Inline(bullet.Groups[1].Value));
            }
            else if (char.IsWhiteSpace(line[0]) && current.HasOpenItem)
            {
                current.Continue(Inline(line.Trim()));
            }
            else
            {
                current.AddItem(Inline(line.Trim()));
            }
        }

        Flush(current, versions);
        return versions
            .GroupBy(v => v.Version)
            .Select(g => g.First())
            .OrderByDescending(v => v.Version)
            .ToList();
    }

    /// <summary>Markdown inline syntax reduced to its text.</summary>
    public static string Inline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var plain = MarkdownLink().Replace(text, "$1");
        plain = Bold().Replace(plain, "$1");
        plain = Code().Replace(plain, "$1");
        return plain.Trim();
    }

    private static Builder? VersionHeading(string line)
    {
        var match = VersionLine().Match(line);
        var version = match.Success ? AppVersion.TryParse(match.Groups[1].Value) : null;
        if (version is null)
        {
            return null;
        }

        var date = match.Groups[2].Value.Trim();
        return new Builder(version, date.Length == 0 ? null : date);
    }

    private static void Flush(Builder? builder, List<ChangelogVersion> versions)
    {
        if (builder is not null)
        {
            versions.Add(builder.Build());
        }
    }

    [GeneratedRegex(@"^##\s+\[?v?(\d{1,9}\.\d{1,9}\.\d{1,9})\]?(?:\s*[-–—]\s*(.*))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s{0,3}\[[^\]]+\]:\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex LinkDefinition();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)\s]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex Bold();

    [GeneratedRegex(@"`([^`]*)`", RegexOptions.CultureInvariant)]
    private static partial Regex Code();

    private sealed class Builder(Version version, string? date)
    {
        private readonly List<ChangelogSection> _sections = [];
        private string? _heading;
        private List<string> _items = [];
        private StringBuilder? _item;

        public bool HasOpenItem => _item is not null;

        public void StartSection(string heading)
        {
            CloseSection();
            _heading = heading;
        }

        public void AddItem(string text)
        {
            CloseItem();
            _item = new StringBuilder(text);
        }

        public void Continue(string text) => _item!.Append(' ').Append(text);

        public ChangelogVersion Build()
        {
            CloseSection();
            return new ChangelogVersion(version, date, _sections);
        }

        private void CloseItem()
        {
            if (_item is not null)
            {
                _items.Add(_item.ToString());
                _item = null;
            }
        }

        private void CloseSection()
        {
            CloseItem();
            if (_items.Count > 0)
            {
                _sections.Add(new ChangelogSection(string.IsNullOrEmpty(_heading) ? null : _heading, _items));
            }

            _heading = null;
            _items = [];
        }
    }
}
