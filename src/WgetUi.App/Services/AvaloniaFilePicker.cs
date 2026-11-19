using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WgetUi.App.Localization;
using WgetUi.Core.PackageLists;

namespace WgetUi.App.Services;

/// <summary><see cref="IFilePicker"/> over the main window's StorageProvider (the system dialogs).</summary>
public sealed class AvaloniaFilePicker : IFilePicker
{
    public Task<string?> PickSaveListAsync(string suggestedFileName) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (StorageProvider() is not { CanSave: true } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Instance["Export_Title"],
            SuggestedFileName = suggestedFileName,
            DefaultExtension = PackageListFile.Extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(Loc.Instance["Export_FileType"]) { Patterns = ["*" + PackageListFile.Extension] }],
        });
        return file?.TryGetLocalPath();
    });

    public Task<string?> PickOpenListAsync() => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (StorageProvider() is not { CanOpen: true } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Instance["Import_Title"],
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Loc.Instance["Import_FileType"]) { Patterns = ["*" + PackageListFile.Extension, "*.json"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    });

    private static IStorageProvider? StorageProvider() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is Window window
            ? TopLevel.GetTopLevel(window)?.StorageProvider
            : null;
}
