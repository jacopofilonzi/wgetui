using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WgetUi.App.Services;
using WgetUi.Core.Models;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.ViewModels;

/// <summary>
/// "Import from URL": asks for an http/https address, downloads the list (read-only) and, once it is read, closes and
/// opens the import popup with it. Errors stay in this popup so the address can be fixed. Closing it cancels the download.
/// </summary>
public sealed partial class ImportUrlViewModel : ListPopupViewModel
{
    private readonly IPackageListDownloader _downloader;
    private readonly Func<PackageList, string, Task> _open;
    private CancellationToken _closed;
    private string? _errorCode;

    /// <param name="downloader">Downloads the list.</param>
    /// <param name="open">Shows the import popup for the downloaded list and its label.</param>
    public ImportUrlViewModel(IPackageListDownloader downloader, Func<PackageList, string, Task> open)
    {
        _downloader = downloader;
        _open = open;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadListCommand))]
    public partial string Address { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadListCommand))]
    public partial bool IsDownloading { get; private set; }

    public string? ErrorText => _errorCode is null ? null : Loc.Error(_errorCode);

    public bool HasError => _errorCode is not null;

    // The overlay cancels this token when the popup closes.
    public override Task LoadAsync(CancellationToken cancellationToken)
    {
        _closed = cancellationToken;
        return Task.CompletedTask;
    }

    public override void OnLanguageChanged() => OnPropertyChanged(nameof(ErrorText));

    partial void OnAddressChanged(string value) => SetError(null);

    private bool CanLoad() => !IsDownloading && !string.IsNullOrWhiteSpace(Address);

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadListAsync()
    {
        if (!HttpPackageListDownloader.TryParseUrl(Address, out var url) || url is null)
        {
            SetError(ErrorCodes.ListUrlInvalid);
            return;
        }

        IsDownloading = true;
        SetError(null);
        try
        {
            var list = await _downloader.DownloadAsync(url, _closed);
            var label = HttpPackageListDownloader.Label(url);
            Choose(() => _open(list, label));
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            // Closed while downloading.
        }
        catch (PackageListException ex)
        {
            SetError(ex.ErrorCode);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void SetError(string? code)
    {
        _errorCode = code;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }
}
