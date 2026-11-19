using Microsoft.Extensions.Logging;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.Core;
using WgetUi.Core.Models;

namespace WgetUi.App.ViewModels;

/// <summary>Builds the package-list popups (export, import) and shows them in the <see cref="PackageListOverlay"/>.</summary>
public sealed class PackageListLauncher
{
    private readonly ManagerRegistry _registry;
    private readonly PackageListOverlay _overlay;
    private readonly IFilePicker _picker;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;

    public PackageListLauncher(ManagerRegistry registry, PackageListOverlay overlay, IFilePicker picker, TimeProvider time, ILoggerFactory loggers)
    {
        _registry = registry;
        _overlay = overlay;
        _picker = picker;
        _time = time;
        _loggers = loggers;
    }

    /// <summary>Shows the export popup for these installed packages.</summary>
    public Task ShowExportAsync(IReadOnlyList<Package> installed) =>
        _overlay.ShowAsync(new ExportViewModel(installed, SourceName, _picker, _time, _loggers.CreateLogger<ExportViewModel>()));

    /// <summary>The display name of a package's source (translated "Local" for <see cref="PackageSource.LocalId"/>).</summary>
    public string SourceName(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return SourceName(package.ManagerId, package.SourceId);
    }

    public string SourceName(string managerId, string sourceId) =>
        sourceId == PackageSource.LocalId
            ? Loc.Instance["Source_Local"]
            : _registry.Sources.FirstOrDefault(s => s.ManagerId == managerId && s.Id == sourceId)?.DisplayName ?? sourceId;
}
