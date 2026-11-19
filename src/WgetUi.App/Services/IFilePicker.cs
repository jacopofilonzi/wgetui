namespace WgetUi.App.Services;

/// <summary>The system file pickers for package lists (implemented with Avalonia's StorageProvider; tests use a fake).</summary>
public interface IFilePicker
{
    /// <summary>Asks where to save a <c>.wgetui</c> list; the local path, or null when cancelled.</summary>
    Task<string?> PickSaveListAsync(string suggestedFileName);

    /// <summary>Asks for a list to import (<c>.wgetui</c> or a <c>winget export</c> <c>.json</c>); the local path, or null.</summary>
    Task<string?> PickOpenListAsync();
}
