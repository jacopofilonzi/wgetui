using Microsoft.Extensions.Logging;
using WgetUi.App.Infrastructure;
using WgetUi.Core.Models;
using WgetUi.Core.Operations;

namespace WgetUi.App.ViewModels;

/// <summary>
/// What Search and Updates call when the user clicks Install / Upgrade / Upgrade all. No confirmation:
/// the agreements modal (if any) is decided by <see cref="OperationRequester"/>.
/// </summary>
public sealed partial class PackageActions
{
    private readonly OperationRequester _requester;
    private readonly SettingsService _settings;
    private readonly ILogger<PackageActions> _logger;

    public PackageActions(OperationRequester requester, SettingsService settings, ILogger<PackageActions> logger)
    {
        _requester = requester;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Returns the request outcome; never throws for a manager failure.</summary>
    public async Task<OperationRequestResult> RequestAsync(IReadOnlyList<Package> packages, OperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(packages);
        try
        {
            return await _requester.RequestAsync(packages, kind, _settings.Current.AutoAcceptAgreements, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            // The queue is shut down (app closing): nothing to queue any more.
            LogRequestFailed(ex, kind, packages.Count);
            return new OperationRequestResult([], false, []);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not queue {Kind} of {Count} package(s)")]
    private partial void LogRequestFailed(Exception ex, OperationKind kind, int count);
}
