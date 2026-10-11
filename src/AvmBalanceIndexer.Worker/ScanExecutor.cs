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

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, options.Value.Concurrency).Select(index => LoopAsync(sweeps: index == 0, stoppingToken)));

    /// <summary>One slot of the executor: claims a scan, runs it, repeats. Slot 0 also sweeps expired scans.</summary>
    private async Task LoopAsync(bool sweeps, CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = clock.GetUtcNow();
                if (sweeps && now - lastCleanup >= CleanupEvery)
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
            catch (Exception ex)
            {
                // Scans are a side feature: whatever goes wrong here (the tables are not there yet because the writer has not applied the
                // schema, the database is away, a bug) must never stop the indexer that shares this process. Wait and try again.
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

    /// <summary>
    /// One job from claim to completion. The job row is the single source of truth: after any failure whose outcome is unknown the step
    /// starts over from what the row says (stored count and cursor), so a retry can neither repeat nor skip rows.
    /// </summary>
    private async Task RunAsync(ScanJob job, CancellationToken stoppingToken)
    {
        var settings = options.Value;
        LogStarted(logger, job.Id, job.Token, job.Round, job.HoldersFound);
        var cursor = job.Cursor;
        var retries = 0;
        var resync = false;
        try
        {
            while (true)
            {
                try
                {
                    if (clock.GetUtcNow() >= job.ExpiresAt)
                    {
                        // The sweeper deletes the job and its result any moment now.
                        LogReleased(logger, job.Id);
                        return;
                    }

                    if (resync)
                    {
                        // Where did the last step really get to? The job row says; it also tells whether it is still ours.
                        if (await jobs.GetJobAsync(job.Id, stoppingToken).ConfigureAwait(false) is not { State: ScanState.Running } current)
                        {
                            LogReleased(logger, job.Id);
                            return;
                        }

                        cursor = current.Cursor;

                        // A heartbeat, so that long retries do not make the scan look abandoned. Rejected when another replica took it over.
                        if (!await jobs.AppendAsync(job.Id, _owner, [], cursor, clock.GetUtcNow(), stoppingToken).ConfigureAwait(false))
                        {
                            LogReleased(logger, job.Id);
                            return;
                        }

                        resync = false;
                    }

                    if (cursor != ScanJob.EndOfList)
                    {
                        var page = await storage.GetHoldersAtRoundAsync(job.Token, job.Round, job.IncludeZero, cursor, settings.PageSize, stoppingToken).ConfigureAwait(false);
                        var next = page.NextCursor ?? ScanJob.EndOfList;
                        if (!await jobs.AppendAsync(job.Id, _owner, page.Items, next, clock.GetUtcNow(), stoppingToken).ConfigureAwait(false))
                        {
                            LogReleased(logger, job.Id);
                            return;
                        }

                        cursor = next;
                        retries = 0;
                        continue;
                    }

                    if (await jobs.CompleteAsync(job.Id, _owner, clock.GetUtcNow(), stoppingToken).ConfigureAwait(false))
                    {
                        LogCompleted(logger, job.Id);
                    }
                    else
                    {
                        LogReleased(logger, job.Id);
                    }

                    return;
                }
                catch (Exception ex) when (IsTransient(ex) && !stoppingToken.IsCancellationRequested)
                {
                    if (++retries > settings.MaxStepRetries)
                    {
                        if (ex is StorageTimeoutException)
                        {
                            // The same step keeps being too slow: retrying elsewhere would not change that.
                            throw;
                        }

                        // An outage, not a verdict on the scan: leave it running; it is resumed after StaleAfter.
                        LogGaveUp(logger, ex, job.Id);
                        return;
                    }

                    LogStepRetry(logger, ex, job.Id, retries);
                    resync = true;
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
            try
            {
                await jobs.FailAsync(job.Id, _owner, Describe(ex), clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                // The job stays running and is picked up again after StaleAfter.
                LogUnavailable(logger, failure);
            }
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

    [LoggerMessage(EventId = 1337, Level = LogLevel.Warning, Message = "Scan {JobId} is paused: the storage stayed unavailable; another replica resumes it later")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 1334, Level = LogLevel.Error, Message = "Scan {JobId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 1335, Level = LogLevel.Warning, Message = "The scan queue could not be reached; retrying")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1336, Level = LogLevel.Information, Message = "Removed {Count} expired scans")]
    private static partial void LogExpired(ILogger logger, int count);
}
