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
        do
        {
            try
            {
                health.ReportChainStatus(await storage.GetAsync(chain.Chain, stoppingToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSampleFailed(logger, ex);
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

    [LoggerMessage(EventId = 1320, Level = LogLevel.Debug, Message = "The chain status could not be sampled")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
