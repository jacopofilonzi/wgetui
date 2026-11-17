using CommunityToolkit.Mvvm.ComponentModel;
using WgetUi.App.Localization;
using WgetUi.Core;

namespace WgetUi.App.ViewModels;

/// <summary>Base for view models; computed texts are refreshed by <see cref="OnLanguageChanged"/>.</summary>
public abstract class ViewModelBase : ObservableObject
{
    protected static Loc Loc => Loc.Instance;

    /// <summary>Translated, one line per failing manager: "winget: …".</summary>
    protected static string? DescribeErrors(ManagerRegistry registry, IReadOnlyList<ManagerError> errors)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            return null;
        }

        return string.Join(
            Environment.NewLine,
            errors.Select(e => Loc.Format("Errors_ManagerLine", registry.Find(e.ManagerId)?.DisplayName ?? e.ManagerId, Loc.Error(e.ErrorCode))));
    }

    /// <summary>Called on the UI thread after the language changed.</summary>
    public virtual void OnLanguageChanged()
    {
    }
}
