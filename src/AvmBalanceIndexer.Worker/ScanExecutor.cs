using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Runs asynchronous holder scans. Every worker replica runs one, not only the leader: a scan only reads chain data and writes its own
/// result, so replicas share the queue (each job has one owner at a time) and a scan survives the death of its owner by being resumed
/// from its checkpoint by another replica.
/// </summary>
public sealed partial class ScanExecutor(
    IChainStorage storage,
    IScanJobStore jobs,
    IStorageFailureClassifier failures,
    IOptions<ScanOptions> options,
    TimeProvider clock,
    ILogger<ScanExecutor> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromMinutes(5);

    private readonly string _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = clock.GetUtcNow();
                if (now - lastCleanup >= CleanupEvery)
                {
                    lastCleanup = now;
                    var removed = await jobs.DeleteExpiredAsync(now, stoppingToken).ConfigureAwait(false);
                    if (removed > 0)
                    {
                        LogExpired(logger, removed);
                    }
                }

                if (await jobs.ClaimNextAsync(_owner, now, now - settings.StaleAfter, stoppingToken).ConfigureAwait(false) is { } job)
                {
                    await RunAsync(job, stoppingToken).ConfigureAwait(false);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                LogUnavailable(logger, ex);
            }

            try
            {
                await Task.Delay(settings.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunAsync(ScanJob job, CancellationToken stoppingToken)
    {
        var settings = options.Value;
        LogStarted(logger, job.Id, job.Token, job.Round, job.HoldersFound);
        var cursor = job.Cursor;
        var retries = 0;
        try
        {
            while (true)
            {
                try
                {
                    var page = await storage.GetHoldersAtRoundAsync(job.Token, job.Round, job.IncludeZero, cursor, settings.PageSize, stoppingToken).ConfigureAwait(false);
                    if (!await jobs.AppendAsync(job.Id, _owner, page.Items, page.NextCursor, clock.GetUtcNow(), stoppingToken).ConfigureAwait(false))
                    {
                        LogReleased(logger, job.Id);
                        return;
                    }

                    retries = 0;
                    cursor = page.NextCursor;
                    if (cursor is null)
                    {
                        await jobs.CompleteAsync(job.Id, _owner, clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);
                        LogCompleted(logger, job.Id);
                        return;
                    }
                }
                catch (Exception ex) when (IsTransient(ex) && !stoppingToken.IsCancellationRequested)
                {
                    if (++retries > settings.MaxStepRetries)
                    {
                        throw;
                    }

                    LogStepRetry(logger, ex, job.Id, retries);
                    await Task.Delay(settings.RetryDelay, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: the checkpoint stays, another replica (or this one after the restart) resumes after StaleAfter.
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            LogFailed(logger, ex, job.Id);
            await jobs.FailAsync(job.Id, _owner, Describe(ex), clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private bool IsTransient(Exception exception) =>
        exception is StorageTimeoutException or IOException or TimeoutException || failures.IsTransient(exception);

    /// <summary>What a requester may read: no stack traces, no connection strings.</summary>
    private static string Describe(Exception exception) => exception switch
    {
        StorageTimeoutException => "The storage kept timing out while reading the holders.",
        RoundNotIndexedException => "The round is not indexed.",
        _ => "The scan failed; see the worker log.",
    };

    [LoggerMessage(EventId = 1330, Level = LogLevel.Information, Message = "Scan {JobId} of {Token} at round {Round} started with {Found} holders already stored")]
    private static partial void LogStarted(ILogger logger, Guid jobId, TokenKey token, ulong round, long found);

    [LoggerMessage(EventId = 1331, Level = LogLevel.Information, Message = "Scan {JobId} completed")]
    private static partial void LogCompleted(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 1332, Level = LogLevel.Information, Message = "Scan {JobId} is no longer owned by this replica (cancelled, expired or taken over)")]
    private static partial void LogReleased(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 1333, Level = LogLevel.Warning, Message = "Scan {JobId} step failed transiently (retry {Retry})")]
    private static partial void LogStepRetry(ILogger logger, Exception exception, Guid jobId, int retry);

    [LoggerMessage(EventId = 1334, Level = LogLevel.Error, Message = "Scan {JobId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 1335, Level = LogLevel.Warning, Message = "The scan queue could not be reached; retrying")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1336, Level = LogLevel.Information, Message = "Removed {Count} expired scans")]
    private static partial void LogExpired(ILogger logger, int count);
}
