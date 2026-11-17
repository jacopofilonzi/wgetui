namespace WgetUi.App.Infrastructure;

/// <summary>Current settings for the whole app; every change is saved right away.</summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store;

    public SettingsService(SettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; }

    public string FilePath => _store.Path;

    /// <summary>Applies a change and saves. Returns false when saving failed (already logged).</summary>
    public bool Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        change(Current);
        return _store.Save(Current);
    }
}
