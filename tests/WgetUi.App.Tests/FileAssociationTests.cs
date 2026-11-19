using WgetUi.App.Infrastructure;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.Tests;

/// <summary>The installer's .wgetui association must start the app with the option it actually reads.</summary>
public sealed class FileAssociationTests
{
    private const string ProgId = "wgetui.PackageList";

    // Only real [Registry] entries: comments and other sections don't count.
    private static List<Dictionary<string, string>> RegistryEntries()
    {
        var entries = new List<Dictionary<string, string>>();
        var inRegistry = false;
        foreach (var raw in File.ReadAllLines(Path.Combine(RepoRoot(), "installer", "wgetui.iss")))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inRegistry = string.Equals(line, "[Registry]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inRegistry || line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            entries.Add(Parse(line));
        }

        return entries;
    }

    // "Name: value; Name: "quoted ""value"""" → dictionary, Inno's doubled quotes undone.
    private static Dictionary<string, string> Parse(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < line.Length)
        {
            var colon = line.IndexOf(':', i);
            var name = line[i..colon].Trim();
            i = colon + 1;
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            var value = new System.Text.StringBuilder();
            if (i < line.Length && line[i] == '"')
            {
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i += 2;
                    }
                    else if (line[i] == '"')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        value.Append(line[i++]);
                    }
                }
            }
            else
            {
                while (i < line.Length && line[i] != ';')
                {
                    value.Append(line[i++]);
                }
            }

            result[name] = value.ToString().Trim();
            while (i < line.Length && (line[i] == ';' || line[i] == ' '))
            {
                i++;
            }
        }

        return result;
    }

    private static Dictionary<string, string> Key(string subkey) =>
        Assert.Single(RegistryEntries(), e => string.Equals(e["Subkey"], subkey, StringComparison.OrdinalIgnoreCase) && e.GetValueOrDefault("ValueName") == "");

    [Fact]
    public void OpenStartsTheAppWithTheImportOption()
    {
        var command = Key($@"Software\Classes\{ProgId}\shell\open\command");

        Assert.Equal("HKCU", command["Root"]);
        Assert.Equal($"\"{{app}}\\wgetui.exe\" {AppPaths.ImportArgument} \"%1\"", command["ValueData"]);
        Assert.Equal(@"C:\x\a.wgetui", AppPaths.FromArgs([AppPaths.ImportArgument, @"C:\x\a.wgetui"]).ImportPath);
    }

    [Fact]
    public void TheExtensionPointsToTheProgIdAndEverythingIsRemovedOnUninstall()
    {
        var extension = Key($@"Software\Classes\{PackageListFile.Extension}");
        Assert.Equal(ProgId, extension["ValueData"]);
        Assert.Contains("uninsdeletevalue", extension["Flags"], StringComparison.Ordinal);

        var progId = Key($@"Software\Classes\{ProgId}");
        Assert.Contains("uninsdeletekey", progId["Flags"], StringComparison.Ordinal);
        Assert.Equal(@"{app}\wgetui.exe,0", Key($@"Software\Classes\{ProgId}\DefaultIcon")["ValueData"]);

        // Per-user only: never a machine-wide association.
        Assert.All(RegistryEntries(), e => Assert.Equal("HKCU", e["Root"]));
        Assert.Contains(File.ReadAllLines(Path.Combine(RepoRoot(), "installer", "wgetui.iss")), l => l.Trim() == "ChangesAssociations=yes");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WgetUi.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (WgetUi.slnx) not found above the test output folder.");
    }
}
