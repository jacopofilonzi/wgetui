namespace WgetUi.Core.Models;

/// <summary>Phase of a running install, upgrade or uninstall.</summary>
public enum OperationStage
{
    Preparing,
    Downloading,
    Installing,
    Finalizing,
    Uninstalling,
}

/// <summary>Progress report of a running operation.</summary>
/// <param name="Stage">Current phase.</param>
/// <param name="Percent">Overall progress 0–100, or null when indeterminate.</param>
public readonly record struct OperationProgress(OperationStage Stage, double? Percent);
