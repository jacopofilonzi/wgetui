using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.Tests;

/// <summary>
/// Every UI string exists in English and Italian, and every key the app uses (in XAML, in code, or built
/// from an error code / state name) is defined.
/// </summary>
public partial class LocalizationTests
{
    private static readonly string AppDir = Path.Combine(FindRepoRoot(), "src", "WgetUi.App");
    private static readonly Dictionary<string, string> English = Load("Strings.resx");
    private static readonly Dictionary<string, string> Italian = Load("Strings.it.resx");

    [Fact]
    public void BothLanguagesHaveTheSameKeys()
    {
        Assert.Empty(English.Keys.Except(Italian.Keys));
        Assert.Empty(Italian.Keys.Except(English.Keys));
    }

    [Fact]
    public void NoValueIsEmpty()
    {
        Assert.Empty(English.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
        Assert.Empty(Italian.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
    }

    [Fact]
    public void FormatPlaceholdersMatchAcrossLanguages()
    {
        var mismatched = English.Keys.Where(k => Placeholders(English[k]) != Placeholders(Italian[k])).ToList();

        Assert.Empty(mismatched);
    }

    [Fact]
    public void EveryKeyUsedInXamlExists()
    {
        var used = Directory.EnumerateFiles(AppDir, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => XamlKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        Assert.DoesNotContain(used, k => !English.ContainsKey(k));
    }

    [Fact]
    public void EveryLiteralKeyUsedInCodeExists()
    {
        var used = Directory.EnumerateFiles(AppDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => CodeKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        Assert.DoesNotContain(used, k => !English.ContainsKey(k));
    }

    [Fact]
    public void EveryErrorCodeHasAMessage()
    {
        var codes = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.DoesNotContain(codes, c => !English.ContainsKey("Error_" + c));
    }

    [Fact]
    public void EveryOperationStateStageAndKindHasALabel()
    {
        var keys = Enum.GetNames<OperationState>().Select(n => "Ops_State_" + n)
            .Concat(Enum.GetNames<OperationStage>().Select(n => "Ops_Stage_" + n))
            .Concat(Enum.GetNames<OperationKind>().Select(n => "Ops_Kind_" + n));

        Assert.DoesNotContain(keys, k => !English.ContainsKey(k));
    }

    private static string Placeholders(string value) =>
        string.Join(',', Placeholder().Matches(value).Select(m => m.Value).Order(StringComparer.Ordinal));

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(AppDir, "Resources", file)).Root!
            .Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty, StringComparer.Ordinal);

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WgetUi.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (WgetUi.slnx) not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\{l:Tr ([A-Za-z0-9_]+)\}")]
    private static partial Regex XamlKey();

    // loc["Key"], Loc["Key"], Loc.Instance["Key"], Format("Key", …), new(value, "Key") for ChoiceOption. A
    // literal followed by "+" is a prefix (e.g. "Ops_State_" + state), covered by the enum test instead.
    [GeneratedRegex(@"(?:[Ll]oc(?:\.Instance)?\[|Format\(|new\([^()]*,\s*)""([A-Za-z0-9_]+)""\s*[\],)]")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
