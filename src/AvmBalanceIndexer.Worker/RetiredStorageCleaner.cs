using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Carries out the database change requests of the release configuration (ADR 0020): data sets listed as retired are removed
/// by the replica that holds the writer lease, and only once the data this deployment now uses follows the chain, so a rebuild
/// that is still backfilling never loses the data it replaces. A failure is retried at the next check and never stops indexing;
/// the service ends when every request is done or was refused for good.
/// </summary>
public sealed partial class RetiredStorageCleaner(
    IEnumerable<IRetiredStorage> retired,
    IChainStorage storage,
    ChainContext chain,
    WorkerHealth health,
    IOptions<WorkerOptions> options,
    ILogger<RetiredStorageCleaner> logger) : BackgroundService
{
    /// <summary>Tests check more often than the configuration (whole seconds) allows.</summary>
    internal TimeSpan? CheckInterval { get; init; }

    /// <summary>What was last logged per request (or for the status read), to log a repeated answer only once.</summary>
    private readonly Dictionary<string, string> _reported = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Providers without retirement (PostgreSQL) register nothing.
        var pending = retired.SelectMany(static provider => provider.Retired.Select(name => (Provider: provider, Name: name))).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", pending.Select(static request => request.Name));
        LogPending(logger, names, chain.Chain);
        using var timer = new PeriodicTimer(CheckInterval ?? TimeSpan.FromSeconds(Math.Max(1, options.Value.RetirementCheckSeconds)));
        try
        {
            do
            {
                bool allowed;
                try
                {
                    allowed = await MayRemoveAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    var level = Level("status", ex.GetType().Name);
                    LogStatusUnreadable(logger, ex, level);
                    continue;
                }

                if (!allowed)
                {
                    continue;
                }

                // Each request stands alone: it is logged when it is done, and one that keeps failing does not hold up the others.
                foreach (var request in pending.ToArray())
                {
                    try
                    {
                        var result = await request.Provider.RemoveRetiredAsync(request.Name, stoppingToken).ConfigureAwait(false);
                        Report(result);
                        if (result.Outcome != RetirementOutcome.InUse)
                        {
                            pending.Remove(request);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        // A drop that timed out usually finished on the server; the next check then finds nothing left to do.
                        var level = Level(request.Name, ex.GetType().Name);
                        LogFailed(logger, ex, level, request.Name);
                    }
                }
            }
            while (pending.Count > 0 && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    /// <summary>Only the writer acts (one replica sends the schema change), and only when the current data has caught up with the chain.</summary>
    private async Task<bool> MayRemoveAsync(CancellationToken cancellationToken) =>
        health.State == WorkerState.Leader
        && await storage.GetAsync(chain.Chain, cancellationToken).ConfigureAwait(false) is { State: IngestionState.Following };

    /// <summary>
    /// A request that waits is checked every minute, possibly for days: the same answer is a warning once and debug detail
    /// afterwards, so the log (which the deploy job reads) says what changed instead of repeating itself.
    /// </summary>
    private LogLevel Level(string subject, string situation)
    {
        if (_reported.TryGetValue(subject, out var previous) && string.Equals(previous, situation, StringComparison.Ordinal))
        {
            return LogLevel.Debug;
        }

        _reported[subject] = situation;
        return LogLevel.Warning;
    }

    private void Report(RetirementResult result)
    {
        switch (result.Outcome)
        {
            case RetirementOutcome.Removed:
                LogRemoved(logger, result.Name);
                break;
            case RetirementOutcome.Absent:
                LogAbsent(logger, result.Name);
                break;
            case RetirementOutcome.InUse:
                var level = Level(result.Name, result.Reason);
                LogInUse(logger, level, result.Name, result.Reason);
                break;
            default:
                LogRefused(logger, result.Name, result.Reason);
                break;
        }
    }

    // The deploy job (.github/workflows/helm-deploy.yml, "Database change requests") finds these lines by "Retired storage" and the
    // words after the name; RetiredStorageCleanerTests pins them.
    [LoggerMessage(EventId = 1340, Level = LogLevel.Information, Message = "Retired storage {Names} will be removed once {Chain} follows the chain and this replica is the writer")]
    private static partial void LogPending(ILogger logger, string names, string chain);

    [LoggerMessage(EventId = 1341, Level = LogLevel.Warning, Message = "Retired storage {Name} was removed; its data is gone for good")]
    private static partial void LogRemoved(ILogger logger, string name);

    [LoggerMessage(EventId = 1342, Level = LogLevel.Information, Message = "Retired storage {Name} does not exist (already removed); it can be taken off the list")]
    private static partial void LogAbsent(ILogger logger, string name);

    [LoggerMessage(EventId = 1343, Message = "Retired storage {Name} is still in use ({Reason}); trying again later")]
    private static partial void LogInUse(ILogger logger, LogLevel level, string name, string reason);

    [LoggerMessage(EventId = 1344, Level = LogLevel.Error, Message = "Retired storage {Name} was not removed: {Reason}. Correct the list of retired storage")]
    private static partial void LogRefused(ILogger logger, string name, string reason);

    [LoggerMessage(EventId = 1345, Message = "Retired storage {Name} could not be removed; trying again later")]
    private static partial void LogFailed(ILogger logger, Exception exception, LogLevel level, string name);

    [LoggerMessage(EventId = 1346, Message = "The chain status could not be read to decide about retired storage; trying again later")]
    private static partial void LogStatusUnreadable(ILogger logger, Exception exception, LogLevel level);
}
