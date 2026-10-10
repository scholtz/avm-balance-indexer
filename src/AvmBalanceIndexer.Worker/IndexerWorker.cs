namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Single ordered writer per chain. Follows rounds from the stored cursor, derives effects and publishes
/// each round only after all required rows are committed. Ingestion loop is implemented in a follow-up issue.
/// </summary>
public sealed partial class IndexerWorker(ILogger<IndexerWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, configuration["Chain"] ?? "unknown", configuration["Storage:Provider"] ?? "unknown");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Indexer worker started for chain {Chain} using provider {Provider}")]
    private static partial void LogStarted(ILogger logger, string chain, string provider);
}
