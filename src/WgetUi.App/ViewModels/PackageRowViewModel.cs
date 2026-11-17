using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.Core.Models;

namespace WgetUi.App.ViewModels;

/// <summary>
/// One package in the Search, Updates or Installed list: its main action (install, upgrade or uninstall), plus the
/// optional details (i) button and ignore/unignore (Updates). Optional handlers left null hide their action.
/// </summary>
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

    /// <summary>Opens the details popup; null when the manager can't describe packages.</summary>
    public Func<PackageRowViewModel, Task>? DetailsHandler { get; init; }

    /// <summary>Ignores this exact update; null where ignoring makes no sense.</summary>
    public Action<PackageRowViewModel>? IgnoreHandler { get; init; }

    /// <summary>Stops ignoring this update.</summary>
    public Action<PackageRowViewModel>? UnignoreHandler { get; init; }

    public bool CanShowDetails => DetailsHandler is not null;

    public bool CanIgnore => IgnoreHandler is not null && AvailableVersion is not null;

    /// <summary>True once the action was queued from this row, to prevent double clicks.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    public partial bool IsQueued { get; set; }

    /// <summary>The user ignored this exact update: the row is greyed and offers "Don't ignore" instead of its action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAction), nameof(RowOpacity))]
    public partial bool IsIgnored { get; set; }

    public bool ShowAction => CanAct && !IsIgnored;

    public double RowOpacity => IsIgnored ? 0.55 : 1.0;

    private bool CanExecuteAct() => CanAct && !IsQueued;

    [RelayCommand(CanExecute = nameof(CanExecuteAct))]
    private Task ActAsync() => _action(this);

    [RelayCommand]
    private Task ShowDetailsAsync() => DetailsHandler?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private void Ignore() => IgnoreHandler?.Invoke(this);

    [RelayCommand]
    private void Unignore() => UnignoreHandler?.Invoke(this);
}
