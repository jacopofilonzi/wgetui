namespace WgetUi.Core.Models;

/// <summary>A package or source agreement as published by the manager (label, optional text and link).</summary>
public sealed record Agreement(string Label, string? Text, string? Url);
