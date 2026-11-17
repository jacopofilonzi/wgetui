using CommunityToolkit.Mvvm.ComponentModel;
using WgetUi.App.Localization;

namespace WgetUi.App.ViewModels;

/// <summary>An entry of the source filter. <see cref="SourceKey"/> null means "all sources".</summary>
public sealed class SourceOption : ObservableObject
{
    private readonly string? _name;

    public SourceOption(string? sourceKey, string? name)
    {
        SourceKey = sourceKey;
        _name = name;
    }

    /// <summary>"managerId/sourceId", or null for all.</summary>
    public string? SourceKey { get; }

    public string Label => _name ?? Loc.Instance["Search_SourceAll"];

    public void Refresh() => OnPropertyChanged(nameof(Label));
}
