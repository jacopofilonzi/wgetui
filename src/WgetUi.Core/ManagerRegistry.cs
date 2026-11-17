using Microsoft.Extensions.Logging;
using WgetUi.Core.Managers;
using WgetUi.Core.Models;

namespace WgetUi.Core;

/// <summary>An error from one manager during a fan-out query.</summary>
/// <param name="ManagerId">Manager that failed.</param>
/// <param name="ErrorCode">One of <see cref="ErrorCodes"/>.</param>
/// <param name="Message">English detail for logs.</param>
public sealed record ManagerError(string ManagerId, string ErrorCode, string Message);

/// <summary>Packages from every manager that answered, plus the errors of those that did not.</summary>
public sealed record AggregateResult(IReadOnlyList<Package> Packages, IReadOnlyList<ManagerError> Errors);

/// <summary>
/// Holds the registered managers. Queries fan out to every available manager in parallel; one manager
/// failing never fails the others, its error is reported next to the results.
/// </summary>
public sealed partial class ManagerRegistry
{
    private readonly ILogger<ManagerRegistry> _logger;

    public ManagerRegistry(IEnumerable<IPackageManager> managers, ILogger<ManagerRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(managers);
        _logger = logger;
        Managers = [.. managers];
        var duplicate = Managers.GroupBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Package manager id '{duplicate.Key}' is registered twice.", nameof(managers));
        }
    }

    public IReadOnlyList<IPackageManager> Managers { get; }

    /// <summary>Sources of every registered manager, in registration order.</summary>
    public IReadOnlyList<PackageSource> Sources => [.. Managers.SelectMany(m => m.Sources)];

    public IPackageManager? Find(string managerId) =>
        Managers.FirstOrDefault(m => string.Equals(m.Id, managerId, StringComparison.Ordinal));

    /// <summary>Initializes every manager in parallel and returns the errors of the unavailable ones.</summary>
    public async Task<IReadOnlyList<ManagerError>> InitializeAsync(CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(Managers.Select(m => InitializeOneAsync(m, cancellationToken))).ConfigureAwait(false);
        return [.. results.OfType<ManagerError>()];
    }

    public Task<AggregateResult> SearchAsync(string query, CancellationToken cancellationToken) =>
        FanOutAsync(ManagerCapabilities.Search, (m, ct) => m.SearchAsync(query, ct), "search", cancellationToken);

    public Task<AggregateResult> GetUpgradesAsync(bool includeUnknown, CancellationToken cancellationToken) =>
        FanOutAsync(ManagerCapabilities.ListUpgrades, (m, ct) => m.GetUpgradesAsync(includeUnknown, ct), "list upgrades", cancellationToken);

    /// <summary>Every installed package from the managers that can list them.</summary>
    public Task<AggregateResult> GetInstalledAsync(CancellationToken cancellationToken) =>
        FanOutAsync(ManagerCapabilities.ListInstalled, (m, ct) => m.GetInstalledAsync(ct), "list installed packages", cancellationToken);

    private async Task<ManagerError?> InitializeOneAsync(IPackageManager manager, CancellationToken cancellationToken)
    {
        try
        {
            var availability = await manager.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (availability.IsAvailable)
            {
                return null;
            }

            LogUnavailable(manager.Id, availability.ErrorCode, availability.Message);
            return new ManagerError(manager.Id, availability.ErrorCode ?? ErrorCodes.ManagerUnavailable, availability.Message ?? "Manager unavailable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A manager must not throw from InitializeAsync, but one bug must not take down the others.
            LogUnexpected(ex, manager.Id, "initialize");
            return new ManagerError(manager.Id, ErrorCodes.Unexpected, $"Initializing {manager.Id} failed: {ex.Message}");
        }
    }

    private async Task<AggregateResult> FanOutAsync(
        ManagerCapabilities capability,
        Func<IPackageManager, CancellationToken, Task<IReadOnlyList<Package>>> query,
        string what,
        CancellationToken cancellationToken)
    {
        var targets = Managers.Where(m => m.Capabilities.HasFlag(capability)).ToList();
        var tasks = targets.Select(m => QueryOneAsync(m, query, what, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var packages = new List<Package>();
        var errors = new List<ManagerError>();
        foreach (var (found, error) in results)
        {
            packages.AddRange(found);
            if (error is not null)
            {
                errors.Add(error);
            }
        }

        return new AggregateResult(packages, errors);
    }

    private async Task<(IReadOnlyList<Package> Packages, ManagerError? Error)> QueryOneAsync(
        IPackageManager manager,
        Func<IPackageManager, CancellationToken, Task<IReadOnlyList<Package>>> query,
        string what,
        CancellationToken cancellationToken)
    {
        if (manager.Availability is { IsAvailable: false } availability)
        {
            return ([], new ManagerError(manager.Id, availability.ErrorCode ?? ErrorCodes.ManagerUnavailable, availability.Message ?? "Manager unavailable."));
        }

        try
        {
            return (await query(manager, cancellationToken).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ([], null);
        }
        catch (PackageManagerException ex)
        {
            LogQueryFailed(ex, manager.Id, what, ex.ErrorCode);
            return ([], new ManagerError(manager.Id, ex.ErrorCode, ex.Message));
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, manager.Id, what);
            return ([], new ManagerError(manager.Id, ErrorCodes.Unexpected, $"{manager.Id} {what} failed: {ex.Message}"));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Package manager {ManagerId} is unavailable: {ErrorCode} {Detail}")]
    private partial void LogUnavailable(string managerId, string? errorCode, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Package manager {ManagerId} failed to {What}: {ErrorCode}")]
    private partial void LogQueryFailed(Exception ex, string managerId, string what, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error from package manager {ManagerId} during {What}")]
    private partial void LogUnexpected(Exception ex, string managerId, string what);
}
