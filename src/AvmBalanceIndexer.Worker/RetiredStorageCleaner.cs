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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Providers without retirement (PostgreSQL) register nothing.
        var pending = retired.Where(static r => r.Retired.Count > 0).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", pending.SelectMany(static r => r.Retired));
        LogPending(logger, names, chain.Chain);
        using var timer = new PeriodicTimer(CheckInterval ?? TimeSpan.FromSeconds(Math.Max(1, options.Value.RetirementCheckSeconds)));
        try
        {
            do
            {
                try
                {
                    if (await MayRemoveAsync(stoppingToken).ConfigureAwait(false))
                    {
                        for (var i = pending.Count - 1; i >= 0; i--)
                        {
                            if (await RemoveAsync(pending[i], stoppingToken).ConfigureAwait(false))
                            {
                                pending.RemoveAt(i);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // A drop that timed out usually finished on the server; the next check then finds nothing left to do.
                    LogFailed(logger, ex);
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

    /// <summary>True when nothing is left to try for this provider.</summary>
    private async Task<bool> RemoveAsync(IRetiredStorage provider, CancellationToken cancellationToken)
    {
        var finished = true;
        foreach (var result in await provider.RemoveRetiredAsync(cancellationToken).ConfigureAwait(false))
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
                    LogInUse(logger, result.Name, result.Reason);
                    finished = false;
                    break;
                default:
                    LogRefused(logger, result.Name, result.Reason);
                    break;
            }
        }

        return finished;
    }

    [LoggerMessage(EventId = 1340, Level = LogLevel.Information, Message = "Retired storage {Names} will be removed once {Chain} follows the chain and this replica is the writer")]
    private static partial void LogPending(ILogger logger, string names, string chain);

    [LoggerMessage(EventId = 1341, Level = LogLevel.Warning, Message = "Retired storage {Name} was removed; its data is gone for good")]
    private static partial void LogRemoved(ILogger logger, string name);

    [LoggerMessage(EventId = 1342, Level = LogLevel.Information, Message = "Retired storage {Name} does not exist (already removed); it can be taken off the list")]
    private static partial void LogAbsent(ILogger logger, string name);

    [LoggerMessage(EventId = 1343, Level = LogLevel.Warning, Message = "Retired storage {Name} is still in use ({Reason}); trying again later")]
    private static partial void LogInUse(ILogger logger, string name, string reason);

    [LoggerMessage(EventId = 1344, Level = LogLevel.Error, Message = "Retired storage {Name} was not removed: {Reason}. Correct the list of retired storage")]
    private static partial void LogRefused(ILogger logger, string name, string reason);

    [LoggerMessage(EventId = 1345, Level = LogLevel.Warning, Message = "Removing retired storage failed; trying again later")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
