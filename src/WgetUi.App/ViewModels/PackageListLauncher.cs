using Microsoft.Extensions.Logging;
using WgetUi.App.Localization;
using WgetUi.App.Services;
using WgetUi.Core;
using WgetUi.Core.Models;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.ViewModels;

/// <summary>
/// Builds the package-list popups (export, import from a file or a URL) and shows them in the <see cref="PackageListOverlay"/>.
/// The entry points: the Installed page's Export / Import buttons, <c>--import &lt;path&gt;</c> at startup, and a second
/// launch handing its <c>--import</c> over (double-click on a <c>.wgetui</c> file). Reading a list is read-only.
/// </summary>
public sealed partial class PackageListLauncher
{
    private readonly ManagerRegistry _registry;
    private readonly PackageListOverlay _overlay;
    private readonly IFilePicker _picker;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly PackageActions? _actions;
    private readonly IPackageListDownloader? _downloader;
    private readonly ILogger _logger;

    /// <param name="registry">Managers.</param>
    /// <param name="overlay">Where the popups are shown.</param>
    /// <param name="picker">The file dialogs.</param>
    /// <param name="time">Today's date (export file name).</param>
    /// <param name="loggers">Loggers of the popups.</param>
    /// <param name="actions">Queues the imported packages; null disables importing (tests that only export).</param>
    /// <param name="downloader">Downloads lists from a URL; null disables "From URL…".</param>
    public PackageListLauncher(ManagerRegistry registry, PackageListOverlay overlay, IFilePicker picker, TimeProvider time, ILoggerFactory loggers, PackageActions? actions = null, IPackageListDownloader? downloader = null)
    {
        _registry = registry;
        _overlay = overlay;
        _picker = picker;
        _time = time;
        _loggers = loggers;
        _actions = actions;
        _downloader = downloader;
        _logger = loggers.CreateLogger<PackageListLauncher>();
    }

    public bool CanImport => _actions is not null;

    public bool CanImportFromUrl => _actions is not null && _downloader is not null;

    /// <summary>Shows the export popup for these installed packages.</summary>
    public Task ShowExportAsync(IReadOnlyList<Package> installed) =>
        _overlay.ShowAsync(new ExportViewModel(installed, SourceName, _picker, _time, _loggers.CreateLogger<ExportViewModel>()));

    /// <summary>"From file…": the open dialog, then the import popup.</summary>
    public async Task ImportFromFileAsync()
    {
        if (await _picker.PickOpenListAsync() is { } path)
        {
            await OpenFileAsync(path);
        }
    }

    /// <summary>Reads a list file and shows the import popup (with the error when it can't be read). Never throws.</summary>
    public async Task OpenFileAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var label = Path.GetFileName(path);
        PackageList list;
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            await using (stream)
            {
                list = await PackageListFile.ReadAsync(stream, CancellationToken.None);
            }
        }
        catch (PackageListException ex)
        {
            LogReadFailed(ex, label, ex.ErrorCode);
            await ShowAsync(ImportViewModel.Failed(label, ex.ErrorCode, _loggers.CreateLogger<ImportViewModel>()));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            LogReadFailed(ex, label, ErrorCodes.ListReadFailed);
            await ShowAsync(ImportViewModel.Failed(label, ErrorCodes.ListReadFailed, _loggers.CreateLogger<ImportViewModel>()));
            return;
        }

        await ShowImportAsync(list, label);
    }

    /// <summary>"From URL…": the address popup; a downloaded list opens the import popup.</summary>
    public Task ImportFromUrlAsync() =>
        _downloader is null ? Task.CompletedTask : ShowAsync(new ImportUrlViewModel(_downloader, ShowImportAsync));

    /// <summary>Shows the import popup for a list already read.</summary>
    public Task ShowImportAsync(PackageList list, string label)
    {
        ArgumentNullException.ThrowIfNull(list);
        return _actions is null
            ? Task.CompletedTask
            : ShowAsync(new ImportViewModel(list, label, _registry, _actions, SourceName, _loggers.CreateLogger<ImportViewModel>()));
    }

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

    // Fire-and-forget callers (startup, second launch) must never see an exception.
    private async Task ShowAsync(ListPopupViewModel popup)
    {
        try
        {
            await _overlay.ShowAsync(popup);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogPopupFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the package list {File}: {ErrorCode}")]
    private partial void LogReadFailed(Exception ex, string file, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "The package list popup failed")]
    private partial void LogPopupFailed(Exception ex);
}
