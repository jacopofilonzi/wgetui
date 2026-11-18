using CommunityToolkit.Mvvm.Input;
using WgetUi.App.Services.AppUpdates;
using WgetUi.App.Services.Changelog;

namespace WgetUi.App.ViewModels;

/// <summary>Content of the "What's new" popup: the notes of some versions (newest first) and a link to the current release.</summary>
/// <remarks>The popup chrome is translated; the notes themselves are English only (CHANGELOG.md), by decision.</remarks>
public sealed partial class WhatsNewViewModel : ViewModelBase
{
    private readonly Version _current;

    public WhatsNewViewModel(Version current, IReadOnlyList<ChangelogVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(versions);
        _current = current;
        Versions = versions;
        ReleasePage = GitHubReleaseSource.ReleasePage(current);
    }

    /// <summary>Raised when the user closes the popup.</summary>
    public event EventHandler? CloseRequested;

    public string Title => Loc.Format("WhatsNew_Title", _current.ToString(3));

    public IReadOnlyList<ChangelogVersion> Versions { get; }

    public bool HasVersions => Versions.Count > 0;

    /// <summary>The current version's release on GitHub.</summary>
    public Uri ReleasePage { get; }

    public override void OnLanguageChanged() => OnPropertyChanged(nameof(Title));

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
