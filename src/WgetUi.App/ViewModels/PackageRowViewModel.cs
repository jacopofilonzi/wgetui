using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.Core.Models;

namespace WgetUi.App.ViewModels;

/// <summary>One package in the Search or Updates list, with its single action (install or upgrade).</summary>
public sealed partial class PackageRowViewModel : ObservableObject
{
    private readonly Func<PackageRowViewModel, Task> _action;

    public PackageRowViewModel(Package package, string sourceName, bool canAct, Func<PackageRowViewModel, Task> action)
    {
        ArgumentNullException.ThrowIfNull(package);
        Package = package;
        SourceName = sourceName;
        CanAct = canAct;
        _action = action;
    }

    public Package Package { get; }

    public string Name => Package.Name;

    public string Id => Package.Id;

    public string? Version => Package.Version;

    public bool IsVersionUnknown => Package.IsVersionUnknown;

    public string? AvailableVersion => Package.AvailableVersion;

    public string SourceId => Package.SourceId;

    public string SourceName { get; }

    /// <summary>The manager supports the action (from its capabilities).</summary>
    public bool CanAct { get; }

    /// <summary>True once the action was queued from this row, to prevent double clicks.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    public partial bool IsQueued { get; set; }

    private bool CanExecuteAct() => CanAct && !IsQueued;

    [RelayCommand(CanExecute = nameof(CanExecuteAct))]
    private Task ActAsync() => _action(this);
}
