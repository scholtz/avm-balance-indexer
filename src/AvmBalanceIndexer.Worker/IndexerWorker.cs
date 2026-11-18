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
    ILogger<IndexerWorker> logger,
    TimeProvider? clock = null,
    string? buildVersion = null,
    IEnumerable<IPartialRebuild>? partialRebuilds = null,
    IOptions<WorkerOptions>? workerOptions = null) : BackgroundService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly string _version = buildVersion ?? BuildVersion.Current;
    private static readonly TimeSpan RebuildLogInterval = TimeSpan.FromSeconds(30);
    private readonly object _reportGate = new();
    private long _rebuildReports;

    /// <summary>Reports on the thread that raised them (<see cref="Progress{T}"/> posts, which loses the order and the last report).</summary>
    private sealed class SynchronousProgress(Action<PartialRebuildProgress> handler) : IProgress<PartialRebuildProgress>
    {
        public void Report(PartialRebuildProgress value) => handler(value);
    }

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

    /// <summary>
    /// A stop of the ledger audit that is recorded in the database and not acknowledged keeps every writer from indexing past the bad round (ADR 0021),
    /// also after a restart: it is the fatal path again, with the same words. Acknowledging a round (<c>Worker:Audit:AcknowledgeRound</c>) is the
    /// operator's decision that the round is fine. A rebuild releases the stop (it deletes the stops after its round).
    /// </summary>
    private async Task CheckAuditStopAsync(CancellationToken cancellationToken)
    {
        if (workerOptions?.Value.Audit.AcknowledgeRound is { } acknowledge && await storage.AcknowledgeAuditStopAsync(acknowledge, cancellationToken).ConfigureAwait(false))
        {
            LogAuditAcknowledged(logger, chain.Chain, acknowledge);
        }

        if (await storage.GetAuditStopAsync(cancellationToken).ConfigureAwait(false) is { } stop)
        {
            throw new InvalidOperationException(
                $"Ledger audit of {chain.Chain} failed at round {stop.Round}: the ledger disagrees at that round and the writer had published up to {stop.PublishedRound}. "
                + $"Find the first wrong round (at or below {stop.Round}; the reason names the last audited round that agreed when one is known, the wrong round can lie before it), fix the cause, then rebuild from a round below it (docs/operations.md), "
                + $"or acknowledge the round (worker.audit.acknowledgeRound) when the ledger side is the one that is wrong. {stop.Reason}");
        }
    }

    /// <summary>
    /// A configured partial rebuild (ADR 0021) runs before the first round of a writer term, whenever there is a request: the storage decides
    /// whether it was carried out already (<see cref="PartialRebuildOutcome.AlreadyDone"/>) or must be refused. A refusal stops the worker:
    /// indexing from genesis into the database because the rebuild was refused would be the opposite of what the operator asked for.
    /// </summary>
    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var rebuild = partialRebuilds?.FirstOrDefault(static r => r.Request is not null);
        if (rebuild?.Request is not { } request)
        {
            return;
        }

        // A request that was carried out does nothing, and needs nothing from the nodes (a stale entry in the values must not stop a worker).
        if (await rebuild.IsCarriedOutAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The source must be a keyspace built from the history the nodes serve: blocks are hash-linked, so the block of its published round
        // has the hash it stores. A mismatch is a different history, not a fetch failure.
        if (await rebuild.ReadSourceCursorAsync(cancellationToken).ConfigureAwait(false) is { } source
            && !(await indexer.GetBlockHashAsync(source.Round, cancellationToken).ConfigureAwait(false)).AsSpan().SequenceEqual(source.Hash.Span))
        {
            throw new InvalidOperationException($"The partial rebuild of {chain.Chain} from {request.Source} is refused: the source was built from another history (its block {source.Round} differs from the one the nodes serve).");
        }

        var hash = await indexer.GetBlockHashAsync(request.Round, cancellationToken).ConfigureAwait(false);
        var lastReport = long.MinValue;
        var progress = new SynchronousProgress(report =>
        {
            // Ranges are copied in parallel: the reports arrive on several threads.
            lock (_reportGate)
            {
                Interlocked.Increment(ref _rebuildReports);
                health.ReportRebuild(report.RangesDone, report.RangesTotal);
                var now = _clock.GetTimestamp();
                if (lastReport == long.MinValue || _clock.GetElapsedTime(lastReport, now) >= RebuildLogInterval)
                {
                    lastReport = now;
                    LogRebuildProgress(logger, chain.Chain, request.Source, report.Stage, report.Rows, report.RangesDone, report.RangesTotal);
                }
            }
        });
        PartialRebuildResult result;
        try
        {
            result = await rebuild.RunAsync(hash, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            health.ReportRebuild(0, 0);
        }

        switch (result.Outcome)
        {
            case PartialRebuildOutcome.Done:
                // What the database held when the indexer last looked is gone: it starts again from the stored state only.
                indexer.Reset();
                LogRebuildDone(logger, chain.Chain, request.Source, request.Round, result.EffectsCopied);
                break;
            case PartialRebuildOutcome.Refused:
                throw new InvalidOperationException($"The partial rebuild of {chain.Chain} from {request.Source} is refused: {result.Reason}");
        }
    }

    private async Task RecordTermAsync(CancellationToken cancellationToken)
    {
        var status = await storage.GetAsync(chain.Chain, cancellationToken).ConfigureAwait(false);
        var fromRound = status is null ? 0 : status.IndexedRound + 1;
        await storage.AppendAsync(new IndexingTerm(fromRound, _version, _clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        LogTerm(logger, chain.Chain, fromRound, _version);
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
        var committedAtLastFailure = indexer.RoundsCommitted + Interlocked.Read(ref _rebuildReports);
        var prepared = false;
        var quota = new QuotaWait(settings.RetryInitialBackoff, settings.QuotaBackoff, _clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Inside the retry loop: a transient failure of the rebuild is retried with the same backoff, and the copy resumes. The term
                // is recorded after the rebuild (so its first round is the one after the copied data, and the copied build records stay), and
                // before anything is indexed (ADR 0021): a database that cannot take the record is a wait like any other transient failure.
                if (!prepared)
                {
                    // The rebuild comes first: it is what releases a recorded stop (the rewind deletes the stops after its round, a new keyspace has none).
                    await PrepareAsync(stoppingToken).ConfigureAwait(false);
                    await CheckAuditStopAsync(stoppingToken).ConfigureAwait(false);
                    await RecordTermAsync(stoppingToken).ConfigureAwait(false);
                    prepared = true;
                }

                await indexer.RunAsync(null, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsQuotaExceeded(ex))
            {
                // The daily quota of a public node: nothing is wrong, and a restart would only spend more requests. Waited out without
                // counting a failure; the status goes stale meanwhile, which is what the alerts look at. Near the quota only a share of
                // the requests is refused: the wait starts short and grows only while nothing is indexed between two refusals (QuotaWait).
                var step = quota.Next(indexer.RoundsCommitted + Interlocked.Read(ref _rebuildReports));
                if (step.GiveUp)
                {
                    throw new InvalidOperationException(
                        $"The node has answered that its quota is used up for more than {QuotaWait.GiveUpAfter.TotalHours:0} hours without any round being indexed: this is not a daily quota that renews itself. Check the plan or the token of the node (Algorand:AlgodUrl).",
                        ex);
                }

                if (step.Log)
                {
                    LogQuotaExceeded(logger, chain.Chain, settings.QuotaBackoff);
                }

                try
                {
                    await Task.Delay(step.Wait, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception ex) when (IsTransient(ex, stoppingToken))
            {
                // Rounds committed since the previous failure mean the indexer is making progress despite the trouble.
                // A copy that reports progress is making progress, also while no round is committed.
                var committed = indexer.RoundsCommitted + Interlocked.Read(ref _rebuildReports);
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
    /// retried like 429 (a 403 that is really a bad credential or IP block ends the worker after MaxConsecutiveFailures); 400, 401 and 404 mean a wrong URL or token and stay fatal.
    /// </summary>
    private bool IsTransient(Exception exception, CancellationToken stoppingToken) =>
        exception is BlockFetchException or HttpRequestException or TimeoutException or IOException
        || exception is Algorand.ApiException { StatusCode: >= 500 or 403 or 408 or 429 }
        || (exception is TaskCanceledException && !stoppingToken.IsCancellationRequested)
        || exception is StorageTimeoutException
        || failures.IsTransient(exception);

    /// <summary>
    /// The answer of a public gateway whose daily quota is used up (Nodely: 403 "Daily free API quota exceeded", https://nodely.io/docs/free/policy).
    /// The algod client reports it as <see cref="NodeQuotaExceededException"/> for every request (also when a caller wraps it). Any other
    /// 403 stays a transient failure that ends the worker when it persists.
    /// </summary>
    private static bool IsQuotaExceeded(Exception exception) =>
        exception is NodeQuotaExceededException || (exception.InnerException is { } inner && IsQuotaExceeded(inner));

    [LoggerMessage(EventId = 1318, Level = LogLevel.Warning, Message = "The node of {Chain} answered that its daily request quota is used up (HTTP 403); indexing waits (up to {Backoff} between two attempts) until the quota is renewed; this line is repeated at most every ten minutes. Use an own node or a paid plan (Algorand:AlgodUrl), or audit fewer followed rounds (raise Worker:Audit:FollowSampleEvery, 0 = none), when this happens every day")]
    private static partial void LogQuotaExceeded(ILogger logger, string chain, TimeSpan backoff);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Indexer worker started for {Chain}")]
    private static partial void LogStarted(ILogger logger, string chain);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "Another replica is the writer of {Chain}; standing by")]
    private static partial void LogStandby(ILogger logger, string chain);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Information, Message = "This replica is now the writer of {Chain}")]
    private static partial void LogLeading(ILogger logger, string chain);

    [LoggerMessage(EventId = 1317, Level = LogLevel.Warning, Message = "The stop of the ledger audit of {Chain} at round {Round} was acknowledged by the configuration; indexing continues")]
    private static partial void LogAuditAcknowledged(ILogger logger, string chain, ulong round);

    [LoggerMessage(EventId = 1315, Level = LogLevel.Information, Message = "Partial rebuild of {Chain} from {Source}: {Stage}, {Rows} rows, {Done}/{Total} ranges")]
    private static partial void LogRebuildProgress(ILogger logger, string chain, string source, string stage, long rows, int done, int total);

    [LoggerMessage(EventId = 1316, Level = LogLevel.Warning, Message = "Partial rebuild of {Chain}: {Source} taken over up to round {Round} ({Effects} effects)")]
    private static partial void LogRebuildDone(ILogger logger, string chain, string source, ulong round, long effects);

    [LoggerMessage(EventId = 1313, Level = LogLevel.Information, Message = "This replica writes {Chain} from round {FromRound} with build {Version}")]
    private static partial void LogTerm(ILogger logger, string chain, ulong fromRound, string version);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning, Message = "The writer lease for {Chain} was lost; indexing stopped and this replica goes back to standby")]
    private static partial void LogLeaseLost(ILogger logger, string chain);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Warning, Message = "The writer lease could not be requested; retrying")]
    private static partial void LogLeaseUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Indexing paused by a transient failure (failure {Failures} without progress); retrying in {Backoff}")]
    private static partial void LogTransientFailure(ILogger logger, Exception exception, TimeSpan backoff, int failures);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Critical, Message = "Indexing of {Chain} stopped after round {IndexedRound} by a failure that retrying cannot fix: {Reason}")]
    private static partial void LogFatal(ILogger logger, Exception exception, string chain, string indexedRound, string reason);
}
