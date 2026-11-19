using CommunityToolkit.Mvvm.ComponentModel;
using WgetUi.App.Localization;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.ViewModels;

/// <summary>What resolving one imported package found (read-only lookups).</summary>
public enum ImportFinding
{
    /// <summary>Still being looked up.</summary>
    Checking,

    /// <summary>The source offers it and it is not installed.</summary>
    NotInstalled,

    /// <summary>Installed (matched by manager, source and id).</summary>
    Installed,

    /// <summary>The source does not offer it.</summary>
    NotFound,

    /// <summary>Its manager or source is not registered, unavailable, or could not be asked.</summary>
    SourceUnavailable,
}

/// <summary>A row's state in the current version mode (what the status pill says).</summary>
public enum ImportRowState
{
    Checking,
    ToInstall,
    Installed,
    NotFound,
    SourceUnavailable,

    /// <summary>Pinned mode only: the pinned version is missing, not offered, or the manager can't install a given version.</summary>
    VersionNotAvailable,
}

/// <summary>One operation the import popup would queue for a row.</summary>
/// <param name="Kind">Install or Upgrade.</param>
/// <param name="Package">The package to act on.</param>
/// <param name="TargetVersion">Installs of a pinned version only.</param>
public sealed record ImportRequest(OperationKind Kind, Package Package, string? TargetVersion);

/// <summary>
/// One package of the import popup. The finding comes from <see cref="Resolve"/>; everything else depends on the mode:
/// </summary>
/// <remarks>
/// Latest versions: not installed → Install (the default version); installed → Upgrade, selectable only when an update
/// is available ("Up to date" otherwise). Pinned versions: Install with <see cref="OperationOptions.TargetVersion"/> =
/// the list's version, when it is offered and the manager declares <see cref="ManagerCapabilities.InstallVersion"/>
/// ("Version not available" otherwise); an installed package is selectable only when the pinned version differs from
/// the installed one ("Same version" otherwise). Not-installed rows start selected, installed ones unselected; the
/// user's choice survives a mode switch that makes a row unselectable for a while.
/// </remarks>
public sealed partial class ImportRowViewModel : ObservableObject
{
    private const string NoVersion = "—";
    private readonly Action _changed;
    private bool _wanted;
    private bool _pinned;

    public ImportRowViewModel(PackageListEntry entry, string sourceName, Action changed)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        SourceName = sourceName;
        _changed = changed;
    }

    public PackageListEntry Entry { get; }

    public ImportFinding Finding { get; private set; } = ImportFinding.Checking;

    /// <summary>The installed package, when <see cref="Finding"/> is <see cref="ImportFinding.Installed"/>.</summary>
    public Package? InstalledPackage { get; private set; }

    /// <summary>What the source offers, when it could be asked (null: unknown or not offered).</summary>
    public PackageOffer? Offer { get; private set; }

    public ManagerCapabilities Capabilities { get; private set; }

    /// <summary>The manager's name, else the installed entry's, else the list's.</summary>
    public string Name => Offer?.Package.Name ?? InstalledPackage?.Name ?? Entry.Name;

    public string Id => Entry.Id;

    public string SourceId => Entry.SourceId;

    public string SourceName { get; }

    /// <summary>"7zip.7zip · winget".</summary>
    public string IdLine => $"{Entry.Id} · {SourceName}";

    public string? PinnedVersion => Entry.Version;

    /// <summary>The version an install would pick today (or the installed one when it is up to date).</summary>
    public string? LatestVersion => Offer?.Package.Version ?? InstalledPackage?.AvailableVersion ?? (InstalledPackage is { AvailableVersion: null } ? InstalledPackage.Version : null);

    public string? InstalledVersion => InstalledPackage?.Version;

    public bool IsPinnedMode => _pinned;

    public ImportRowState State => ComputeState();

    public bool CanSelect => Request is not null;

    /// <summary>Queued by "Install selected"; always false when the row can't be selected.</summary>
    public bool IsSelected
    {
        get => _wanted && CanSelect;
        set
        {
            if (!CanSelect || _wanted == value)
            {
                return;
            }

            _wanted = value;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>What "Install selected" queues for this row in the current mode, or null when it can't act.</summary>
    public ImportRequest? Request => ComputeRequest();

    /// <summary>The big version: what would be installed in this mode.</summary>
    public string MainVersion => _pinned
        ? PinnedVersion ?? NoVersion
        : LatestVersion ?? (Finding is ImportFinding.NotInstalled or ImportFinding.Installed ? Loc.Instance["Import_LatestUnknown"] : NoVersion);

    /// <summary>The small version: the other mode's ("pinned 26.00" / "latest 26.06").</summary>
    public string? OtherVersion => _pinned
        ? LatestVersion is { } latest ? Loc.Instance.Format("Import_LatestVersion", latest) : null
        : PinnedVersion is { } pinned ? Loc.Instance.Format("Import_PinnedVersion", pinned) : null;

    public bool HasOtherVersion => OtherVersion is not null;

    public string StatusText => State switch
    {
        ImportRowState.Checking => Loc.Instance["Import_StatusChecking"],
        ImportRowState.ToInstall => Loc.Instance["Import_StatusToInstall"],
        ImportRowState.Installed => InstalledVersion is { } version ? Loc.Instance.Format("Import_StatusInstalled", version) : Loc.Instance["Import_StatusInstalledUnknown"],
        ImportRowState.NotFound => Loc.Instance["Import_StatusNotFound"],
        ImportRowState.SourceUnavailable => Loc.Instance["Import_StatusSourceUnavailable"],
        _ => Loc.Instance["Import_StatusVersionNotAvailable"],
    };

    /// <summary>Why an installed row can't be selected: "Up to date" / "Same version".</summary>
    public string? Note => State != ImportRowState.Installed || CanSelect
        ? null
        : Loc.Instance[_pinned ? "Import_NoteSameVersion" : "Import_NoteUpToDate"];

    public bool HasNote => Note is not null;

    public bool IsChecking => State == ImportRowState.Checking;

    public bool IsToInstall => State == ImportRowState.ToInstall;

    public bool IsInstalled => State == ImportRowState.Installed;

    public bool IsProblem => State is ImportRowState.NotFound or ImportRowState.VersionNotAvailable;

    public bool IsUnavailable => State == ImportRowState.SourceUnavailable;

    /// <summary>Records what the lookups found; selects the row when it is not installed.</summary>
    public void Resolve(ImportFinding finding, Package? installed, PackageOffer? offer, ManagerCapabilities capabilities)
    {
        Finding = finding;
        InstalledPackage = installed;
        Offer = offer;
        Capabilities = capabilities;
        _wanted = finding == ImportFinding.NotInstalled;
        OnPropertyChanged(string.Empty);
    }

    public void SetMode(bool pinned)
    {
        _pinned = pinned;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Changes the user's choice without telling the popup (it recounts once after a bulk change).</summary>
    internal void SetWanted(bool value)
    {
        if (CanSelect && _wanted != value)
        {
            _wanted = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    // The package to install: the source's (name, latest version), else the installed one, else the list's.
    private Package InstallPackage() =>
        Offer?.Package ?? InstalledPackage ?? new Package(Entry.ManagerId, Entry.SourceId, Entry.Id, Entry.Name, null);

    private bool PinnedIsInstalled =>
        PinnedVersion is not null && string.Equals(PinnedVersion, InstalledVersion, StringComparison.OrdinalIgnoreCase);

    // Offer null with a found package means the manager can't look versions up: trust the list.
    private bool PinnedIsOffered =>
        PinnedVersion is not null
        && Capabilities.HasFlag(ManagerCapabilities.Install)
        && Capabilities.HasFlag(ManagerCapabilities.InstallVersion)
        && (Offer is null || Offer.Offers(PinnedVersion));

    private ImportRowState ComputeState() => Finding switch
    {
        ImportFinding.Checking => ImportRowState.Checking,
        ImportFinding.NotFound => ImportRowState.NotFound,
        ImportFinding.SourceUnavailable => ImportRowState.SourceUnavailable,
        _ when _pinned && !(Finding == ImportFinding.Installed && PinnedIsInstalled) && !PinnedIsOffered => ImportRowState.VersionNotAvailable,
        ImportFinding.Installed => ImportRowState.Installed,
        _ => ImportRowState.ToInstall,
    };

    private ImportRequest? ComputeRequest()
    {
        switch (ComputeState())
        {
            case ImportRowState.ToInstall when _pinned:
                return new ImportRequest(OperationKind.Install, InstallPackage(), PinnedVersion);
            case ImportRowState.ToInstall when Capabilities.HasFlag(ManagerCapabilities.Install):
                return new ImportRequest(OperationKind.Install, InstallPackage(), null);
            case ImportRowState.Installed when _pinned && !PinnedIsInstalled:
                return new ImportRequest(OperationKind.Install, InstallPackage(), PinnedVersion);
            case ImportRowState.Installed when !_pinned && InstalledPackage is { AvailableVersion: not null } installed && Capabilities.HasFlag(ManagerCapabilities.Upgrade):
                return new ImportRequest(OperationKind.Upgrade, installed, null);
            default:
                return null;
        }
    }
}
