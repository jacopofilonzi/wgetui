using Microsoft.Extensions.Logging;
using WgetUi.App.Services;
using WgetUi.Core;

namespace WgetUi.App.ViewModels;

/// <summary>Shows a package details popup (implemented by the App's dialog; tests use a fake).</summary>
public interface IPackageDetailsPresenter
{
    /// <summary>Shows the popup, starts loading, and returns once it closed and its chosen action (if any) was started.</summary>
    Task ShowAsync(PackageDetailsViewModel details);
}

/// <summary>Builds the details view model for a row and hands it to the presenter.</summary>
public sealed class PackageDetailsLauncher
{
    private readonly ManagerRegistry _registry;
    private readonly IPackageDetailsPresenter _presenter;
    private readonly ILogger<PackageDetailsViewModel> _logger;
    private readonly AutoUpdateService? _autoUpdates;

    /// <param name="registry">Managers (details, capabilities).</param>
    /// <param name="presenter">Shows the popup.</param>
    /// <param name="logger">Logger of the popups.</param>
    /// <param name="autoUpdates">Rules behind the "Automatic updates" section; null hides it (tests that don't need it).</param>
    public PackageDetailsLauncher(ManagerRegistry registry, IPackageDetailsPresenter presenter, ILogger<PackageDetailsViewModel> logger, AutoUpdateService? autoUpdates = null)
    {
        _registry = registry;
        _presenter = presenter;
        _logger = logger;
        _autoUpdates = autoUpdates;
    }

    public Task ShowAsync(PackageRowViewModel row, DetailsContext context, PackageDetailsActions actions)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _presenter.ShowAsync(new PackageDetailsViewModel(row.Package, row.SourceName, context, _registry, actions, _logger, _autoUpdates));
    }
}
