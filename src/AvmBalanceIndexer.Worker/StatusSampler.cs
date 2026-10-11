using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Reads the published status every few seconds so indexed round, lag and the age of the last update are available as metrics on
/// every replica (the leader writes them, standbys only read). A failing read just keeps the previous sample.
/// </summary>
public sealed partial class StatusSampler(
    IChainStorage storage,
    ChainContext chain,
    WorkerHealth health,
    IOptions<WorkerOptions> options,
    ILogger<StatusSampler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.StatusSampleSeconds));
        using var timer = new PeriodicTimer(interval);
        var failing = false;
        do
        {
            try
            {
                health.ReportChainStatus(await storage.GetAsync(chain.Chain, stoppingToken).ConfigureAwait(false));
                failing = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The previous sample stays (and the counter rises); only the first failure of a streak is a warning.
                health.ReportSampleFailure();
                LogSampleFailed(logger, ex, failing ? LogLevel.Debug : LogLevel.Warning);
                failing = true;
            }
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(EventId = 1320, Message = "The chain status could not be sampled; the indexer metrics keep their last values")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception, LogLevel level);
}
