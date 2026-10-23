using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Single ordered writer for one chain: waits for the writer lease (so several replicas can run, one active), prepares the
/// schema, then follows the chain from the stored cursor. Losing the lease stops indexing at once and the replica goes back to
/// waiting; a replica that never holds the lease simply stands by.
/// Transient failures (network, database availability) are retried with backoff, and the backoff resets whenever rounds
/// were indexed in between. Data problems such as a chain divergence stop the process immediately so that nothing is
/// written on top of inconsistent state, and so does a long run of failures without progress, which makes a permanently
/// broken source visible to the orchestrator instead of retrying silently forever.
/// </summary>
public sealed partial class IndexerWorker(
    RoundIndexer indexer,
    IChainStorage storage,
    ChainContext chain,
    IOptions<IndexerOptions> options,
    IHostApplicationLifetime lifetime,
    IWriterLeaseProvider leases,
    IStorageFailureClassifier failures,
    WorkerHealth health,
    ILogger<IndexerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        health.ReportRoundsSource(() => indexer.RoundsCommitted);
        try
        {
            await LeadWhenPossibleAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            var indexedRound = await TryReadIndexedRoundAsync().ConfigureAwait(false);
            var reason = ex.Message;
            LogFatal(logger, ex, chain.Chain, indexedRound, reason);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }

    private async Task LeadWhenPossibleAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var schemaReady = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            IWriterLease? lease;
            try
            {
                if (!schemaReady)
                {
                    // Every replica prepares the schema (idempotent), because asking for the lease already needs its tables.
                    // Inside the retry handling: a database that is still starting or failing over is a wait, not a crash.
                    await storage.EnsureSchemaAsync(stoppingToken).ConfigureAwait(false);
                    schemaReady = true;
                    LogStarted(logger, chain.Chain);
                }

                lease = await leases.TryAcquireAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsTransient(ex, stoppingToken))
            {
                // A database that is still starting or failing over is a wait, not a crash.
                health.ReportTransientFailure();
                LogLeaseUnavailable(logger, ex);
                await Task.Delay(settings.LeaseRetryInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            if (lease is null)
            {
                if (health.State != WorkerState.Standby)
                {
                    LogStandby(logger, chain.Chain);
                }

                health.BecameStandby();
                await Task.Delay(settings.LeaseRetryInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            var lost = false;
            await using (lease.ConfigureAwait(false))
            {
                health.BecameLeader();
                LogLeading(logger, chain.Chain);
                using var term = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lease.Lost);
                try
                {
                    await FollowAsync(term.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (lease.Lost.IsCancellationRequested && !stoppingToken.IsCancellationRequested
                    && (ex is OperationCanceledException or ChainDivergenceException || IsTransient(ex, stoppingToken)))
                {
                    // A commit of a writer that has just lost the lease fails against the new writer's cursor (divergence): that
                    // is the safety net working, not a data problem.
                }

                if (lease.Lost.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    health.LostLease();
                    LogLeaseLost(logger, chain.Chain);
                    lost = true;
                }
            }

            if (lost)
            {
                // A lease that keeps failing must not turn into a tight acquire/lose loop.
                await Task.Delay(settings.LeaseRetryInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The published round for the fatal log; the database may be the very reason indexing stopped, so this never throws.</summary>
    private async Task<string> TryReadIndexedRoundAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var status = await storage.GetAsync(chain.Chain, timeout.Token).ConfigureAwait(false);
            return status is null ? "none" : status.IndexedRound.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private async Task FollowAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var backoff = settings.RetryInitialBackoff;
        var failures = 0;
        var committedAtLastFailure = indexer.RoundsCommitted;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await indexer.RunAsync(null, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsTransient(ex, stoppingToken))
            {
                // Rounds committed since the previous failure mean the indexer is making progress despite the trouble.
                var committed = indexer.RoundsCommitted;
                if (committed != committedAtLastFailure)
                {
                    committedAtLastFailure = committed;
                    failures = 0;
                    backoff = settings.RetryInitialBackoff;
                }

                failures++;
                if (failures >= settings.MaxConsecutiveFailures)
                {
                    throw new InvalidOperationException($"Indexing made no progress in {failures} consecutive attempts.", ex);
                }

                health.ReportTransientFailure();
                LogTransientFailure(logger, ex, backoff, failures);
                try
                {
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutdown, or the lease was lost while waiting.
                    return;
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, settings.RetryMaxBackoff.Ticks));
            }
        }
    }

    /// <summary>
    /// Network, node and database trouble that a later attempt can overcome: archival fetch failures, algod errors and
    /// timeouts (a timed-out request throws a cancellation that did not come from shutdown), and transient database errors.
    /// A public algod behind a gateway answers 403 (often with an empty body) while it throttles or blocks a client, so 403 is
    /// retried like 429; 400, 401 and 404 mean a wrong URL or token and stay fatal.
    /// </summary>
    private bool IsTransient(Exception exception, CancellationToken stoppingToken) =>
        exception is BlockFetchException or HttpRequestException or TimeoutException or IOException
        || exception is Algorand.ApiException { StatusCode: >= 500 or 403 or 408 or 429 }
        || (exception is TaskCanceledException && !stoppingToken.IsCancellationRequested)
        || exception is StorageTimeoutException
        || failures.IsTransient(exception);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Indexer worker started for {Chain}")]
    private static partial void LogStarted(ILogger logger, string chain);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "Another replica is the writer of {Chain}; standing by")]
    private static partial void LogStandby(ILogger logger, string chain);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Information, Message = "This replica is now the writer of {Chain}")]
    private static partial void LogLeading(ILogger logger, string chain);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning, Message = "The writer lease for {Chain} was lost; indexing stopped and this replica goes back to standby")]
    private static partial void LogLeaseLost(ILogger logger, string chain);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Warning, Message = "The writer lease could not be requested; retrying")]
    private static partial void LogLeaseUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Indexing paused by a transient failure (failure {Failures} without progress); retrying in {Backoff}")]
    private static partial void LogTransientFailure(ILogger logger, Exception exception, TimeSpan backoff, int failures);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Critical, Message = "Indexing of {Chain} stopped after round {IndexedRound} by a failure that retrying cannot fix: {Reason}")]
    private static partial void LogFatal(ILogger logger, Exception exception, string chain, string indexedRound, string reason);
}
