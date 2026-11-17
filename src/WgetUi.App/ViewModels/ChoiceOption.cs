using CommunityToolkit.Mvvm.ComponentModel;
using WgetUi.App.Localization;

namespace WgetUi.App.ViewModels;

/// <summary>A settings choice: stored value plus the resource key of its label.</summary>
public sealed class ChoiceOption(string value, string labelKey) : ObservableObject
{
    public string Value { get; } = value;

    public string Label => Loc.Instance[labelKey];

    public void Refresh() => OnPropertyChanged(nameof(Label));
}
