using WgetUi.App.Platform;

namespace WgetUi.App.Services;

/// <summary>A program to start again after an upgrade: executable, its original arguments, working directory.</summary>
public sealed record RelaunchTarget(string ExecutablePath, string Arguments, string WorkingDirectory);

/// <summary>
/// Pure decisions for "relaunch apps closed by an upgrade": which running processes belong to a package
/// (by install folder) and may be restarted, and which of them are missing after the upgrade.
/// </summary>
/// <remarks>
/// Conservative on purpose; a process is skipped when any of these holds: another session (services run in
/// session 0); this app; path, command line or elevation unreadable, or elevated (we would restart it
/// without its rights); an installer/updater/uninstaller by name; a child of another selected process
/// (Chromium/Electron helpers, restarted by their parent). Install folders that are a drive root or a shared
/// system folder are refused: they would match unrelated programs.
/// </remarks>
public static class RelaunchPlanner
{
    private static readonly string[] InstallerNameParts = ["unins", "setup", "install", "update", "msiexec"];

    public static bool IsUsableInstallFolder(string? folder, IEnumerable<string> broadFolders)
    {
        ArgumentNullException.ThrowIfNull(broadFolders);
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return false;
        }

        var normalized = Normalize(folder);
        return !string.Equals(normalized, Normalize(Path.GetPathRoot(folder) ?? folder), StringComparison.OrdinalIgnoreCase)
            && !broadFolders.Any(b => !string.IsNullOrEmpty(b) && string.Equals(normalized, Normalize(b), StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<RelaunchTarget> Select(IReadOnlyList<ProcessSnapshot> processes, string installFolder, int ownProcessId, int ownSessionId, string? ownExecutable)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var folder = Normalize(installFolder);
        var candidates = processes.Where(p =>
                p.SessionId == ownSessionId
                && p.Id != ownProcessId
                && p.IsElevated == false
                && p.ExecutablePath is not null
                && p.CommandLine is not null
                && !string.Equals(p.ExecutablePath, ownExecutable, StringComparison.OrdinalIgnoreCase)
                && p.ExecutablePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                && !LooksLikeInstaller(p.ExecutablePath))
            .ToList();
        var ids = candidates.Select(p => p.Id).ToHashSet();

        return [.. candidates
            .Where(p => p.ParentId is not { } parent || !ids.Contains(parent))
            .Select(p => new RelaunchTarget(p.ExecutablePath!, ArgumentsOf(p.CommandLine!), Path.GetDirectoryName(p.ExecutablePath!) ?? folder))
            .Distinct()];
    }

    /// <summary>Targets whose executable is not running any more (in the current session).</summary>
    public static IReadOnlyList<RelaunchTarget> Missing(IReadOnlyList<RelaunchTarget> targets, IReadOnlyList<ProcessSnapshot> processesNow)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(processesNow);
        var running = processesNow.Where(p => p.ExecutablePath is not null).Select(p => p.ExecutablePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. targets.Where(t => !running.Contains(t.ExecutablePath))];
    }

    /// <summary>The command line without its first token (the program), as Windows parses argv[0].</summary>
    public static string ArgumentsOf(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var text = commandLine.TrimStart();
        int end;
        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            end = close < 0 ? text.Length : close + 1;
        }
        else
        {
            var space = text.IndexOfAny([' ', '\t']);
            end = space < 0 ? text.Length : space;
        }

        return text[end..].Trim();
    }

    private static bool LooksLikeInstaller(string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath);
        return InstallerNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string folder) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
}
