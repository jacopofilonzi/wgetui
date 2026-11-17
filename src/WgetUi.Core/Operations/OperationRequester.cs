using Microsoft.Extensions.Logging;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.Core.Operations;

/// <summary>Outcome of <see cref="OperationRequester.RequestAsync"/>.</summary>
/// <param name="Queued">Operations put in the queue (empty when declined).</param>
/// <param name="Declined">The user refused the agreements: nothing was queued, nothing accepted.</param>
/// <param name="AgreementErrors">Packages whose agreements could not be read (queued without accepting).</param>
public sealed record OperationRequestResult(IReadOnlyList<QueuedOperation> Queued, bool Declined, IReadOnlyList<ManagerError> AgreementErrors);

/// <summary>
/// Turns a click on Install / Upgrade / Upgrade all into queued operations, deciding about agreements.
/// </summary>
/// <remarks>
/// Rules (the user's decision, manager-independent):
/// - no generic confirmation: clicking queues directly;
/// - with automatic acceptance on, everything is queued accepting package and source agreements;
/// - otherwise agreements are fetched (read-only); if any package or source has some, one modal lists all
///   of them for the whole request. Accept → queue everything, accepting only for the packages whose
///   agreements were shown. Cancel → nothing is queued and nothing is accepted;
/// - a package whose agreements cannot be read is queued without accepting: the manager then fails it
///   with <see cref="ErrorCodes.AgreementsNotAccepted"/> if acceptance was actually needed.
/// </remarks>
public sealed partial class OperationRequester
{
    private readonly ManagerRegistry _registry;
    private readonly OperationQueue _queue;
    private readonly IAgreementsConfirmer _confirmer;
    private readonly ILogger<OperationRequester> _logger;

    public OperationRequester(ManagerRegistry registry, OperationQueue queue, IAgreementsConfirmer confirmer, ILogger<OperationRequester> logger)
    {
        _registry = registry;
        _queue = queue;
        _confirmer = confirmer;
        _logger = logger;
    }

    public async Task<OperationRequestResult> RequestAsync(IReadOnlyList<Package> packages, OperationKind kind, bool autoAcceptAgreements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Count == 0)
        {
            return new OperationRequestResult([], false, []);
        }

        if (autoAcceptAgreements)
        {
            return new OperationRequestResult(_queue.EnqueueMany(packages, kind, OperationOptions.AcceptAll), false, []);
        }

        var errors = new List<ManagerError>();
        var agreements = new List<PackageAgreements>(packages.Count);
        foreach (var package in packages)
        {
            agreements.Add(await FetchAsync(package, errors, cancellationToken).ConfigureAwait(false));
        }

        var toAccept = agreements.Where(a => a.RequiresAcceptance).ToList();
        if (toAccept.Count > 0 && !await _confirmer.ConfirmAsync(toAccept, cancellationToken).ConfigureAwait(false))
        {
            LogDeclined(toAccept.Count);
            return new OperationRequestResult([], true, errors);
        }

        var queued = agreements
            .Select(a => _queue.Enqueue(a.Package, kind, new OperationOptions(a.PackageTerms.Count > 0, a.SourceTerms.Count > 0)))
            .ToList();
        return new OperationRequestResult(queued, false, errors);
    }

    private async Task<PackageAgreements> FetchAsync(Package package, List<ManagerError> errors, CancellationToken cancellationToken)
    {
        var manager = _registry.Find(package.ManagerId);
        if (manager is null || !manager.Capabilities.HasFlag(ManagerCapabilities.Agreements))
        {
            return PackageAgreements.None(package);
        }

        try
        {
            return await manager.GetAgreementsAsync(package, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PackageManagerException ex)
        {
            LogFetchFailed(ex, package.Key, ex.ErrorCode);
            errors.Add(new ManagerError(manager.Id, ex.ErrorCode, ex.Message));
            return PackageAgreements.None(package);
        }
        catch (Exception ex)
        {
            LogFetchFailed(ex, package.Key, ErrorCodes.Unexpected);
            errors.Add(new ManagerError(manager.Id, ErrorCodes.Unexpected, $"Reading agreements of {package.Key} failed: {ex.Message}"));
            return PackageAgreements.None(package);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User declined the agreements of {Count} package(s); nothing queued")]
    private partial void LogDeclined(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read agreements of {PackageKey}: {ErrorCode}; queuing without accepting")]
    private partial void LogFetchFailed(Exception ex, string packageKey, string errorCode);
}
