using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WgetUi.App.Infrastructure;

/// <summary>Loads and saves <see cref="AppSettings"/> (settings.json). A broken file falls back to defaults.</summary>
public sealed partial class SettingsStore
{
    private readonly string _path;
    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = paths.SettingsFile;
        _logger = logger;
    }

    public string Path => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogLoadFailed(ex, _path);
            return new AppSettings();
        }
    }

    /// <summary>Writes atomically (temp file + move) so a crash never leaves a half-written file.</summary>
    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex, _path);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read settings from {Path}; using defaults")]
    private partial void LogLoadFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save settings to {Path}")]
    private partial void LogSaveFailed(Exception ex, string path);
}
