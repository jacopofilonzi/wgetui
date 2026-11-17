using System.Text.Json.Serialization;

namespace WgetUi.App.Infrastructure;

/// <summary>User settings, persisted as JSON. Unknown values fall back to defaults when applied.</summary>
public sealed class AppSettings
{
    public const string System = "system";

    /// <summary>"system", "en" or "it".</summary>
    public string Language { get; set; } = System;

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = System;

    /// <summary>Accept package and source agreements without showing them. Off by default.</summary>
    public bool AutoAcceptAgreements { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
