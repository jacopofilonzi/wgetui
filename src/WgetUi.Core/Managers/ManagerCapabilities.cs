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

    /// <summary>Can tell where an installed package lives on disk (see <see cref="IPackageManager.GetInstallLocationAsync"/>).</summary>
    InstallLocation = 1 << 8,

    /// <summary>Can list every installed package (see <see cref="IPackageManager.GetInstalledAsync"/>).</summary>
    ListInstalled = 1 << 9,

    /// <summary>Can uninstall a package (see <see cref="IPackageManager.UninstallAsync"/>).</summary>
    Uninstall = 1 << 10,

    /// <summary>Can describe a package: description, links, available versions (see <see cref="IPackageManager.GetDetailsAsync"/>).</summary>
    Details = 1 << 11,

    /// <summary>Can install a specific version (<see cref="Models.OperationOptions.TargetVersion"/>).</summary>
    InstallVersion = 1 << 12,

    /// <summary>Can look a package up by exact id in one of its sources (see <see cref="IPackageManager.FindInSourceAsync"/>).</summary>
    FindById = 1 << 13,
}
