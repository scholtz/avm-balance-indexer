using System.Data.Common;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Single ordered writer for one chain: prepares the schema, then follows the chain from the stored cursor.
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
    ILogger<IndexerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await storage.EnsureSchemaAsync(stoppingToken).ConfigureAwait(false);
            LogStarted(logger, chain.Chain);
            await FollowAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            LogFatal(logger, ex);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
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

                LogTransientFailure(logger, ex, backoff, failures);
                await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, settings.RetryMaxBackoff.Ticks));
            }
        }
    }

    /// <summary>
    /// Network, node and database trouble that a later attempt can overcome: archival fetch failures, algod errors and
    /// timeouts (a timed-out request throws a cancellation that did not come from shutdown), and transient database errors.
    /// </summary>
    private static bool IsTransient(Exception exception, CancellationToken stoppingToken) =>
        exception is BlockFetchException or HttpRequestException or TimeoutException or IOException
        || exception is Algorand.ApiException { StatusCode: >= 500 or 408 or 429 }
        || (exception is TaskCanceledException && !stoppingToken.IsCancellationRequested)
        || exception is DbException { IsTransient: true };

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Indexer worker started for {Chain}")]
    private static partial void LogStarted(ILogger logger, string chain);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Indexing paused by a transient failure (failure {Failures} without progress); retrying in {Backoff}")]
    private static partial void LogTransientFailure(ILogger logger, Exception exception, TimeSpan backoff, int failures);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Critical, Message = "Indexing stopped by a failure that retrying cannot fix")]
    private static partial void LogFatal(ILogger logger, Exception exception);
}
