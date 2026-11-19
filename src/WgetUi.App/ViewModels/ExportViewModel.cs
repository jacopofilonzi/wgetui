using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.App.Services;
using WgetUi.Core.Models;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.ViewModels;

/// <summary>
/// The "Export packages" popup: the installed packages some source offers (never <see cref="PackageSource.LocalId"/>:
/// another machine could not install them), all selected, with a name/id filter and "Select all". Save asks where
/// (system dialog) and writes a <c>.wgetui</c> list with the installed versions; nothing else is touched.
/// </summary>
public sealed partial class ExportViewModel : ListPopupViewModel
{
    private readonly IFilePicker _picker;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly List<ExportRowViewModel> _rows;
    private string? _errorCode;

    /// <param name="installed">The installed packages (the Installed page's list).</param>
    /// <param name="sourceName">Display name of a package's source.</param>
    /// <param name="picker">The save dialog.</param>
    /// <param name="time">Today's date, for the suggested file name.</param>
    /// <param name="logger">Logger.</param>
    public ExportViewModel(IEnumerable<Package> installed, Func<Package, string> sourceName, IFilePicker picker, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(sourceName);
        _picker = picker;
        _time = time;
        _logger = logger;
        _rows = [.. installed
            .Where(p => p.SourceId != PackageSource.LocalId)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new ExportRowViewModel(p, sourceName(p), OnRowSelectionChanged))];
        ApplyFilter();
    }

    /// <summary>The rows matching <see cref="Filter"/>.</summary>
    public ObservableCollection<ExportRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    public int TotalCount => _rows.Count;

    public int SelectedCount => _rows.Count(r => r.IsSelected);

    public string SelectedText => Loc.Format("List_Selected", SelectedCount, TotalCount);

    public bool ShowNone => _rows.Count == 0;

    /// <summary>Tri-state over the visible rows: all, none, or some (null). Setting it selects or clears them all.</summary>
    public bool? SelectAll
    {
        get
        {
            var selected = Rows.Count(r => r.IsSelected);
            return selected == 0 ? false : selected == Rows.Count ? true : null;
        }

        set
        {
            foreach (var row in Rows)
            {
                row.SetSelected(value == true);
            }

            OnRowSelectionChanged();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; set; }

    public string? ErrorText => _errorCode is null ? null : Loc.Error(_errorCode);

    public bool HasError => _errorCode is not null;

    /// <summary>"wgetui-packages-2026-11-19.wgetui".</summary>
    public string SuggestedFileName => $"wgetui-packages-{_time.GetLocalNow():yyyy-MM-dd}{PackageListFile.Extension}";

    /// <summary>The selected packages as list entries, in display order.</summary>
    public IReadOnlyList<PackageListEntry> SelectedEntries() =>
        [.. _rows.Where(r => r.IsSelected).Select(r => new PackageListEntry(r.Package.ManagerId, r.Package.SourceId, r.Package.Id, r.Package.Name, r.Package.Version))];

    public override void OnLanguageChanged() => OnPropertyChanged(string.Empty);

    partial void OnFilterChanged(string value) => ApplyFilter();

    private bool CanSave() => !IsSaving && SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsSaving = true;
        SetError(null);
        try
        {
            var path = await _picker.PickSaveListAsync(SuggestedFileName);
            if (path is null)
            {
                return;
            }

            var entries = SelectedEntries();
            await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                PackageListFile.Write(stream, entries, _time.GetUtcNow(), $"wgetui {AppVersion.Text}");
            }

            LogExported(_logger, entries.Count, path);
            RequestClose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LogExportFailed(_logger, ex);
            SetError(ErrorCodes.ListWriteFailed);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void SetError(string? code)
    {
        _errorCode = code;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        Rows.Clear();
        foreach (var row in _rows.Where(r => filter.Length == 0
            || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || r.Id.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(SelectAll));
    }

    private void OnRowSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectAll));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        SaveCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Exported {Count} package(s) to {Path}")]
    private static partial void LogExported(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the package list")]
    private static partial void LogExportFailed(ILogger logger, Exception ex);
}

/// <summary>One package of the export popup.</summary>
public sealed partial class ExportRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected = true;

    public ExportRowViewModel(Package package, string sourceName, Action changed)
    {
        ArgumentNullException.ThrowIfNull(package);
        Package = package;
        SourceName = sourceName;
        _changed = changed;
    }

    public Package Package { get; }

    public string Name => Package.Name;

    public string Id => Package.Id;

    public string SourceId => Package.SourceId;

    public string SourceName { get; }

    public string? Version => Package.Version;

    public bool IsVersionUnknown => Package.IsVersionUnknown;

    /// <summary>Exported; every row starts selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _changed();
            }
        }
    }

    /// <summary>Changes the selection without telling the popup (it recounts once after a bulk change).</summary>
    internal void SetSelected(bool value)
    {
        if (IsSelected != value)
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}
