using System.Reflection;

namespace WgetUi.App.Infrastructure;

/// <summary>
/// The running version as x.y.z, read from the informational version (<c>&lt;Version&gt;</c> in
/// Directory.Build.props; the SDK may append "+commit", which is dropped).
/// </summary>
public static class AppVersion
{
    /// <summary>"x.y.z", or "?" when the assembly carries no readable version.</summary>
    public static string Text { get; } = Read();

    /// <summary>The same as a <see cref="System.Version"/> (0.0.0 when unreadable, so every release is newer).</summary>
    public static Version Value { get; } = TryParse(Text) ?? new Version(0, 0, 0);

    /// <summary>Parses "x.y.z" (exactly three numeric parts); null otherwise.</summary>
    public static Version? TryParse(string? text)
    {
        if (text is null || text.Split('.').Length != 3 || !Version.TryParse(text, out var version))
        {
            return null;
        }

        return version;
    }

    private static string Read() =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
}
