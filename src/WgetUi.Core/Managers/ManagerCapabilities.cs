namespace WgetUi.Core.Managers;

/// <summary>What a manager can do. The UI reads these to decide which actions to show.</summary>
[Flags]
public enum ManagerCapabilities
{
    None = 0,
    Search = 1 << 0,
    Install = 1 << 1,
    ListUpgrades = 1 << 2,
    Upgrade = 1 << 3,

    /// <summary>Reports a progress percentage while installing or upgrading.</summary>
    Progress = 1 << 4,

    /// <summary>A running install or upgrade can be cancelled.</summary>
    Cancel = 1 << 5,

    /// <summary>Can list upgrades for packages whose installed version is unknown.</summary>
    IncludeUnknownVersions = 1 << 6,

    /// <summary>Can report package and source agreements before an install (see <see cref="IPackageManager.GetAgreementsAsync"/>).</summary>
    Agreements = 1 << 7,
}
