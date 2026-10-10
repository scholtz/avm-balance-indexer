using System.Data.Common;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;

namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Single ordered writer for one chain: prepares the schema, then follows the chain from the stored cursor.
/// Transient failures (network, database availability) are retried with backoff; data problems such as a chain
/// divergence stop the process so that nothing is ever written on top of inconsistent state.
/// </summary>
public sealed partial class IndexerWorker(
    RoundIndexer indexer,
    IChainStorage storage,
    IHostApplicationLifetime lifetime,
    ILogger<IndexerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await storage.EnsureSchemaAsync(stoppingToken).ConfigureAwait(false);
            LogStarted(logger);

            var backoff = InitialBackoff;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await indexer.RunAsync(null, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    LogTransientFailure(logger, ex, backoff);
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                }
            }
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

    private static bool IsTransient(Exception exception) =>
        exception is BlockFetchException or HttpRequestException or TimeoutException or IOException
        || exception is DbException { IsTransient: true };

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Indexer worker started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Indexing paused by a transient failure; retrying in {Backoff}")]
    private static partial void LogTransientFailure(ILogger logger, Exception exception, TimeSpan backoff);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Critical, Message = "Indexing stopped by a failure that retrying cannot fix")]
    private static partial void LogFatal(ILogger logger, Exception exception);
}
