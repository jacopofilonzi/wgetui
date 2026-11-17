using CommunityToolkit.Mvvm.Input;
using WgetUi.Core.Models;

namespace WgetUi.App.ViewModels;

/// <summary>One ignored update in Settings, with "Restore".</summary>
public sealed partial class IgnoredUpdateItemViewModel
{
    private readonly Action<IgnoredUpdate> _restore;

    public IgnoredUpdateItemViewModel(IgnoredUpdate record, Action<IgnoredUpdate> restore)
    {
        ArgumentNullException.ThrowIfNull(record);
        Record = record;
        _restore = restore;
    }

    public IgnoredUpdate Record { get; }

    public string Name => string.IsNullOrWhiteSpace(Record.Name) ? Record.PackageId : Record.Name;

    public string PackageId => Record.PackageId;

    public string Version => Record.Version;

    [RelayCommand]
    private void Restore() => _restore(Record);
}
