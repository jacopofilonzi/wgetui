using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WgetUi.App.ViewModels;

/// <summary>What the tray icon shows as a dot. Order = priority (highest last).</summary>
public enum TrayIconState
{
    None,
    Updates,
    Running,
    Failure,
    AdminPrompt,
}

/// <summary>
/// State of the notification-area icon: the dot, the tooltip and the "Upgrade all (N)" entry, computed from
/// the Updates list and the queue. The App's tray host renders it; no Avalonia type here, so it is testable.
/// </summary>
public sealed partial class TrayViewModel : ViewModelBase
{
    public TrayViewModel(UpdatesViewModel updates, OperationsViewModel operations)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(operations);
        Updates = updates;
        Operations = operations;
        updates.PropertyChanged += OnSourceChanged;
        updates.UpgradeAllCommand.CanExecuteChanged += (_, _) => Notify();
        updates.RefreshCommand.CanExecuteChanged += (_, _) => Notify();
        operations.PropertyChanged += OnSourceChanged;
    }

    /// <summary>Raised on the UI thread whenever anything the tray shows may have changed.</summary>
    public event EventHandler? Changed;

    public UpdatesViewModel Updates { get; }

    public OperationsViewModel Operations { get; }

    /// <summary>An installer is waiting on the UAC prompt (set by the admin-prompt watcher).</summary>
    [ObservableProperty]
    public partial bool IsAdminPromptPending { get; set; }

    public TrayIconState State => Compute(IsAdminPromptPending, Operations.HasUnseenFailure, Operations.IsRunning, Updates.Count);

    public string ToolTip
    {
        get
        {
            var parts = new List<string> { "wgetui" };
            if (Updates.Count > 0)
            {
                parts.Add(Updates.Count == 1 ? Loc["Tray_Updates_One"] : Loc.Format("Tray_Updates_Many", Updates.Count));
            }

            if (Operations.PendingCount > 0)
            {
                parts.Add(Loc.Format("Tray_Queued", Operations.PendingCount));
            }

            return string.Join(" · ", parts);
        }
    }

    public string UpgradeAllHeader => Loc.Format("Tray_UpgradeAll", Updates.Count);

    public bool CanUpgradeAll => Updates.UpgradeAllCommand.CanExecute(null);

    public bool CanCheckForUpdates => Updates.RefreshCommand.CanExecute(null);

    /// <summary>Priority when several apply: admin prompt > failure > running > updates > none.</summary>
    public static TrayIconState Compute(bool adminPrompt, bool failure, bool running, int updates) =>
        adminPrompt ? TrayIconState.AdminPrompt
        : failure ? TrayIconState.Failure
        : running ? TrayIconState.Running
        : updates > 0 ? TrayIconState.Updates
        : TrayIconState.None;

    public void UpgradeAll()
    {
        if (CanUpgradeAll)
        {
            _ = Updates.UpgradeAllCommand.ExecuteAsync(null);
        }
    }

    public void CheckForUpdates()
    {
        if (CanCheckForUpdates)
        {
            _ = Updates.RefreshCommand.ExecuteAsync(null);
        }
    }

    public override void OnLanguageChanged() => Notify();

    partial void OnIsAdminPromptPendingChanged(bool value) => Notify();

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) => Notify();

    private void Notify()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(UpgradeAllHeader));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
