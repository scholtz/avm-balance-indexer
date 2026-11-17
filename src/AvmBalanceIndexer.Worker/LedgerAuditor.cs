using System.Globalization;
using System.Net;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker;

/// <summary>
/// Audits the rounds the writer has published against the ledger (ADR 0021, "Finding R early"): the end-of-round balances of every account the
/// ledger changed in a round must be what the database holds for that round. While the writer follows the tip one round in <see cref="AuditOptions.FollowSampleEvery"/> is audited, while it
/// backfills (also the replay after a partial rebuild) one round in <see cref="AuditOptions.BackfillSampleEvery"/>. The first disagreement is logged,
/// recorded in the database (so that a restart does not index past it) and, by default, stops the worker.
/// The audit reads rounds that are already published: it detects a bad round, it does not keep it from being served. A node that does not answer is
/// never a failure; only a document that was read and disagrees is a mismatch.
/// </summary>
public sealed partial class LedgerAuditor(
    ILedgerDeltaSource deltas,
    IChainStorage storage,
    ChainContext chain,
    WorkerHealth health,
    IOptions<WorkerOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<LedgerAuditor> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const int ReadsInFlight = 16;
    private const int NoDeltasToSwitchOff = 20;
    private const int FailuresBeforeSkipping = 5;
    private const ulong NodeLagRounds = 20;
    private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private ulong? _audited;
    private ulong? _sampled;
    private int _consecutiveNoDeltas;
    private long _auditedRounds;
    private int _failuresOfRound;
    private ulong _failingRound;
    private ulong? _reportedMismatch;
    private long? _lastWarning;
    private long? _pausedAt;
    private TimeSpan _pause;
    private ulong? _lastAgreed;

    /// <summary>Tests pass a shorter pause than the configuration (whole seconds) allows.</summary>
    internal TimeSpan? Interval { get; init; }

    /// <summary>How long nothing is asked after the node refused for its daily quota (403): the indexer needs the requests more.</summary>
    internal TimeSpan QuotaPause { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>How long nothing is asked after the node throttled (429, a limit per second); the round is then asked for again.</summary>
    internal TimeSpan ThrottlePause { get; init; } = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Audit;
        if (!settings.Enabled)
        {
            LogDisabled(logger, chain.Chain);
            return;
        }

        LogStarted(logger, chain.Chain, settings.FollowSampleEvery, settings.BackfillSampleEvery, settings.StopOnMismatch);
        using var timer = new PeriodicTimer(Interval ?? TimeSpan.FromSeconds(Math.Max(1, settings.IntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    if (!await PassAsync(settings, stoppingToken).ConfigureAwait(false))
                    {
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // The database or the node was not reachable: no statement about any round.
                    health.AuditSkipped();
                    Warn(ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>One look at the published status. False ends the service (the audit is off, or the worker was told to stop).</summary>
    private async Task<bool> PassAsync(AuditOptions settings, CancellationToken cancellationToken)
    {
        if (health.State != WorkerState.Leader)
        {
            return true;
        }

        if (_pausedAt is { } pausedAt)
        {
            if (_time.GetElapsedTime(pausedAt) < _pause)
            {
                return true;
            }

            _pausedAt = null;
            if (_audited is null)
            {
                LogResumed(logger, chain.Chain);
            }
        }

        var status = await storage.GetAsync(chain.Chain, cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            return true;
        }

        // History is not audited: the audit starts at the round the writer had published when it became active.
        _audited ??= status.IndexedRound;
        _sampled ??= status.IndexedRound;

        if (status.State == IngestionState.Following)
        {
            var budget = Math.Max(1, settings.MaxRoundsPerPass);
            var every = (ulong)Math.Max(0, settings.FollowSampleEvery);
            for (var round = _audited.Value + 1; round <= status.IndexedRound; round++)
            {
                if (every == 0 || round % every != 0)
                {
                    _audited = round;
                    continue;
                }

                if (budget-- <= 0)
                {
                    break;
                }

                var step = await AuditRoundAsync(round, status.IndexedRound, settings, countNoDeltas: true, cancellationToken).ConfigureAwait(false);
                if (step == Step.Stop)
                {
                    return false;
                }

                if (step == Step.Retry)
                {
                    break;
                }

                _audited = round;
            }

            return !NodeHasNoDeltas();
        }

        // Backfilling (also a replay after a partial rebuild): one round of every BackfillSampleEvery, at most one request per pass.
        // A rebuild moves the published round back: the replay is sampled from there.
        _audited = status.IndexedRound;
        if (status.IndexedRound < _sampled)
        {
            _sampled = status.IndexedRound;
            _lastAgreed = null;
        }

        if (settings.BackfillSampleEvery > 0 && status.State == IngestionState.Backfilling)
        {
            var every = (ulong)settings.BackfillSampleEvery;
            var next = ((_sampled.Value / every) + 1) * every;
            if (next <= status.IndexedRound)
            {
                // A node that keeps deltas of recent rounds only answers 404 for old samples: that says nothing about the tip.
                var step = await AuditRoundAsync(next, status.IndexedRound, settings, countNoDeltas: false, cancellationToken).ConfigureAwait(false);
                if (step == Step.Stop)
                {
                    return false;
                }

                if (step != Step.Retry)
                {
                    _sampled = next;
                }
            }
            else
            {
                _sampled = Math.Max(_sampled.Value, status.IndexedRound / every * every);
            }
        }

        return !NodeHasNoDeltas();
    }

    private enum Step
    {
        Done,
        Retry,
        Stop,
    }

    private bool NodeHasNoDeltas()
    {
        if (_consecutiveNoDeltas < NoDeltasToSwitchOff || _auditedRounds > 0)
        {
            return false;
        }

        LogNoDeltas(logger, chain.Chain, NoDeltasToSwitchOff);
        return true;
    }

    /// <summary>True while the budget of retries for this round is left (and counts the retry).</summary>
    private bool ShouldRetry(ulong round)
    {
        if (_failingRound != round)
        {
            _failingRound = round;
            _failuresOfRound = 0;
        }

        if (++_failuresOfRound < FailuresBeforeSkipping)
        {
            return true;
        }

        _failuresOfRound = 0;
        return false;
    }

    private async Task<Step> AuditRoundAsync(ulong round, ulong published, AuditOptions settings, bool countNoDeltas, CancellationToken cancellationToken)
    {
        LedgerDelta? delta;
        try
        {
            delta = await deltas.TryGetAsync(round, cancellationToken).ConfigureAwait(false);
        }
        catch (NodeQuotaExceededException)
        {
            // The daily quota of a public node is used up (and it counts the refused request too): the audit stops asking for a while
            // and continues with the round that is published then. The rounds in between are not audited.
            _pausedAt = _time.GetTimestamp();
            _pause = QuotaPause;
            _audited = null;
            health.AuditSkipped();
            LogQuotaPause(logger, chain.Chain, round, _pause);
            return Step.Retry;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests)
        {
            // A limit per second: the same round is asked for again after a short pause, nothing is skipped.
            _pausedAt = _time.GetTimestamp();
            _pause = ThrottlePause;
            LogThrottled(logger, chain.Chain, round, _pause);
            return Step.Retry;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Not a statement about the round: tried again at the next pass, and given up on only after a few failures of the same round.
            Warn(ex);
            if (ShouldRetry(round))
            {
                return Step.Retry;
            }

            health.AuditSkipped();
            return Step.Done;
        }

        if (delta is null)
        {
            // A 404 for one of the newest published rounds can only mean that the node (behind a load balancer, or simply a little behind the
            // block source) has not reached it yet: tried again like a failure. For an older round the node keeps no deltas (or none at all).
            if (published - round < NodeLagRounds && ShouldRetry(round))
            {
                return Step.Retry;
            }

            if (countNoDeltas)
            {
                _consecutiveNoDeltas++;
            }

            health.AuditSkipped();
            return Step.Done;
        }

        _consecutiveNoDeltas = 0;
        var mismatches = await LedgerAudit.CompareAsync(storage, delta, round, ReadsInFlight, cancellationToken).ConfigureAwait(false);
        _auditedRounds++;
        health.ReportAudited(round);
        if (mismatches.Count == 0)
        {
            _lastAgreed = round;
            return Step.Done;
        }

        var first = mismatches[0];
        var description = string.Join("; ", mismatches.Take(5).Select(Describe)) + (mismatches.Count > 5 ? $"; and {mismatches.Count - 5} more" : string.Empty);
        if (settings.IgnoreRounds.Contains(round))
        {
            health.AuditIgnoredMismatch();
            LogIgnored(logger, chain.Chain, round, description);
            return Step.Done;
        }

        // Counted and logged once, also when recording the stop fails and the round is looked at again.
        if (_reportedMismatch != round)
        {
            _reportedMismatch = round;
            health.AuditMismatch();
            LogMismatch(logger, chain.Chain, round, mismatches.Count, ReferenceAddress(first.Account), first.Token.ToString(), first.Indexed.ToString(), first.Ledger.ToString(), description);
        }

        if (!settings.StopOnMismatch)
        {
            return Step.Done;
        }

        // Not every round is audited (sampling, pauses, a node without an answer): the round before this one is not known to be good.
        // A sample of an older round (backfill after falling behind) can fail below a round that agreed earlier: that one says nothing.
        var good = _lastAgreed is { } agreed && agreed < round ? agreed.ToString(CultureInfo.InvariantCulture) : "none known below that round";
        var reason = $"round {round}: {description}; last audited round that agreed: {good}";
        try
        {
            await storage.RecordAuditStopAsync(new AuditStop(round, reason, published, _time.GetUtcNow(), false), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The stop is not durable yet: the round is looked at again, and not counted again.
            LogStopNotRecorded(logger, ex, chain.Chain, round);
            return Step.Retry;
        }

        LogStop(logger, chain.Chain, round, published, good, reason);
        Environment.ExitCode = 1;
        lifetime.StopApplication();
        return Step.Stop;
    }

    private static string ReferenceAddress(Address account) => new Algorand.Address(account.ToArray()).EncodeAsString();

    private static string Describe(LedgerMismatch m) =>
        string.Create(CultureInfo.InvariantCulture, $"{ReferenceAddress(m.Account)} {m.Token}: indexed {m.Indexed}, ledger {m.Ledger}");

    /// <summary>A node that does not answer is logged at most once every ten minutes.</summary>
    private void Warn(Exception exception)
    {
        var now = _time.GetTimestamp();
        if (_lastWarning is { } last && _time.GetElapsedTime(last, now) < WarnEvery)
        {
            return;
        }

        _lastWarning = now;
        LogUnavailable(logger, exception, chain.Chain);
    }

    [LoggerMessage(EventId = 1350, Level = LogLevel.Information, Message = "The ledger audit of {Chain} is switched off (Worker:Audit:Enabled)")]
    private static partial void LogDisabled(ILogger logger, string chain);

    [LoggerMessage(EventId = 1351, Level = LogLevel.Information, Message = "The ledger audit of {Chain} runs: one followed round in {FollowSampleEvery} (0 = none) and one backfilled round in {SampleEvery} are compared with the ledger deltas of the node; stop on a mismatch: {StopOnMismatch}")]
    private static partial void LogStarted(ILogger logger, string chain, int followSampleEvery, int sampleEvery, bool stopOnMismatch);

    [LoggerMessage(EventId = 1352, Level = LogLevel.Information, Message = "The node of {Chain} offers no ledger deltas (none for {Count} rounds in a row); the audit is off")]
    private static partial void LogNoDeltas(ILogger logger, string chain, int count);

    [LoggerMessage(EventId = 1353, Level = LogLevel.Warning, Message = "The ledger deltas of {Chain} could not be read; the round is tried again, an unreachable node is no mismatch (logged at most every ten minutes)")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string chain);

    [LoggerMessage(EventId = 1354, Level = LogLevel.Error, Message = "Ledger audit of {Chain}: round {Round} disagrees with the ledger in {Count} balances; first: {Account} {Token}, indexed {Indexed}, ledger {Ledger} ({Details})")]
    private static partial void LogMismatch(ILogger logger, string chain, ulong round, int count, string account, string token, string indexed, string ledger, string details);

    [LoggerMessage(EventId = 1355, Level = LogLevel.Warning, Message = "Ledger audit of {Chain}: round {Round} disagrees with the ledger but is listed in Worker:Audit:IgnoreRounds ({Details})")]
    private static partial void LogIgnored(ILogger logger, string chain, ulong round, string details);

    [LoggerMessage(EventId = 1359, Level = LogLevel.Information, Message = "The ledger audit of {Chain} resumes at the published round")]
    private static partial void LogResumed(ILogger logger, string chain);

    [LoggerMessage(EventId = 1358, Level = LogLevel.Warning, Message = "The daily quota of the node of {Chain} is used up (HTTP 403 for the ledger deltas of round {Round}); the ledger audit pauses for {Pause} and does not audit the rounds published meanwhile")]
    private static partial void LogQuotaPause(ILogger logger, string chain, ulong round, TimeSpan pause);

    [LoggerMessage(EventId = 1360, Level = LogLevel.Information, Message = "The node of {Chain} throttles (HTTP 429 for the ledger deltas of round {Round}); the ledger audit asks again in {Pause}")]
    private static partial void LogThrottled(ILogger logger, string chain, ulong round, TimeSpan pause);

    [LoggerMessage(EventId = 1357, Level = LogLevel.Warning, Message = "The ledger audit of {Chain} could not record its stop at round {Round}; the database is tried again at the next pass and the worker keeps running until then")]
    private static partial void LogStopNotRecorded(ILogger logger, Exception exception, string chain, ulong round);

    [LoggerMessage(EventId = 1356, Level = LogLevel.Critical, Message = "Ledger audit of {Chain} failed at round {Round}: the ledger disagrees at that round and this writer has published up to {Published}; the last audited round that agreed is {Good}. Fix the cause, then rebuild from a round that was audited and agreed (docs/operations.md). {Reason}")]
    private static partial void LogStop(ILogger logger, string chain, ulong round, ulong published, string good, string reason);
}
