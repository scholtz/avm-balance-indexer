using System.Diagnostics.Metrics;
using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Worker;

public enum WorkerState
{
    /// <summary>Not yet decided whether this replica leads (database still starting, first lease attempt pending).</summary>
    Starting = 0,

    /// <summary>Another replica holds the writer lease; this one waits to take over.</summary>
    Standby = 1,

    /// <summary>This replica holds the lease and indexes.</summary>
    Leader = 2,
}

/// <summary>
/// What the worker is doing, for the health endpoints and for metrics. Instruments live on the meter
/// <c>AvmBalanceIndexer.Worker</c>; attach any OpenTelemetry/Prometheus exporter to that name.
/// </summary>
public sealed class WorkerHealth : IDisposable
{
    public const string MeterName = "AvmBalanceIndexer.Worker";

    private readonly Meter _meter;
    private int _state;
    private long _leasesAcquired;
    private long _leasesLost;
    private long _transientFailures;
    private long _sampleFailures;
    private long _auditedRound;
    private long _auditedRounds;
    private long _auditMismatches;
    private long _auditIgnored;
    private long _auditSkipped;
    private readonly object _rebuildGate = new();
    private (int Done, int Total) _rebuild;
    private Func<long> _roundsCommitted = static () => 0;
    private ChainStatus? _chainStatus;
    private Func<DateTimeOffset> _now = static () => DateTimeOffset.UtcNow;

    /// <param name="meterName">Only tests pass another name, to observe one instance without the others running in parallel.</param>
    public WorkerHealth(string meterName = MeterName)
    {
        _meter = new Meter(meterName);
        _meter.CreateObservableGauge("avm.worker.is_leader", () => State == WorkerState.Leader ? 1 : 0, description: "1 while this replica holds the writer lease");
        _meter.CreateObservableCounter("avm.worker.leases_acquired", () => Interlocked.Read(ref _leasesAcquired), description: "Times this replica became the writer");
        _meter.CreateObservableCounter("avm.worker.leases_lost", () => Interlocked.Read(ref _leasesLost), description: "Times the writer lease was lost while indexing");
        _meter.CreateObservableCounter("avm.indexer.status_sample_failures", () => Interlocked.Read(ref _sampleFailures), description: "Failed reads of the published status; while they rise the avm.indexer gauges are stale");
        _meter.CreateObservableCounter("avm.worker.transient_failures", () => Interlocked.Read(ref _transientFailures), description: "Retried indexing failures");
        _meter.CreateObservableCounter("avm.worker.rounds_committed", () => _roundsCommitted(), description: "Rounds this process committed");
        _meter.CreateObservableGauge("avm.audit.last_round", () => Interlocked.Read(ref _auditedRound), description: "Last round the ledger audit compared with the ledger (ADR 0021); 0 before the first");
        _meter.CreateObservableCounter("avm.audit.rounds", () => Interlocked.Read(ref _auditedRounds), description: "Rounds the ledger audit compared with the ledger");
        _meter.CreateObservableCounter("avm.audit.mismatches", () => Interlocked.Read(ref _auditMismatches), description: "Rounds at which the database disagreed with the ledger; the worker stops at the first (Worker:Audit:StopOnMismatch)");
        _meter.CreateObservableCounter("avm.audit.ignored_mismatches", () => Interlocked.Read(ref _auditIgnored), description: "Mismatches at rounds listed in Worker:Audit:IgnoreRounds");
        _meter.CreateObservableCounter("avm.audit.skipped", () => Interlocked.Read(ref _auditSkipped), description: "Rounds or passes the audit could not compare (no deltas, node or database unreachable); never a failure");
        _meter.CreateObservableGauge("avm.rebuild.ranges_done", () => { lock (_rebuildGate) { return _rebuild.Done; } }, description: "Ranges of the stage of a partial rebuild that is running that are finished (ADR 0021)");
        _meter.CreateObservableGauge("avm.rebuild.ranges_total", () => { lock (_rebuildGate) { return _rebuild.Total; } }, description: "Ranges of the stage of a partial rebuild that is running; 0 when none runs");

        // What the database says, sampled by StatusSampler: the same numbers the API reports, as time series.
        _meter.CreateObservableGauge("avm.indexer.indexed_round", () => Measure(static s => (long)s.IndexedRound), description: "Published round");
        _meter.CreateObservableGauge("avm.indexer.target_round", () => Measure(static s => s.TargetRound is { } t ? (long)t : null), description: "Latest round of the chain as last seen by the indexer");
        _meter.CreateObservableGauge("avm.indexer.lag_rounds", () => Measure(static s => s.TargetRound is null ? null : (long)s.Lag), description: "Rounds between the chain tip and the published round");
        _meter.CreateObservableGauge(
            "avm.indexer.status_age_seconds",
            () => Measure(s => (long)Math.Max(0, (_now() - s.UpdatedAt).TotalSeconds)),
            unit: "s",
            description: "Seconds since the writer last updated the status (rises when indexing is stuck)");
    }

    public WorkerState State
    {
        get => (WorkerState)Volatile.Read(ref _state);
        private set => Volatile.Write(ref _state, (int)value);
    }

    public long LeasesAcquired => Interlocked.Read(ref _leasesAcquired);

    public long LeasesLost => Interlocked.Read(ref _leasesLost);

    /// <summary>The replica can serve its role: it knows whether it leads or stands by.</summary>
    public bool Ready => State != WorkerState.Starting;

    public void ReportRoundsSource(Func<long> roundsCommitted) => _roundsCommitted = roundsCommitted;

    /// <summary>The last status read from the database (null when nothing is published yet).</summary>
    public void ReportChainStatus(ChainStatus? status) => Volatile.Write(ref _chainStatus, status);

    /// <summary>Tests replace the clock to make the status age deterministic.</summary>
    internal void UseClock(Func<DateTimeOffset> now) => _now = now;

    private IEnumerable<Measurement<long>> Measure(Func<ChainStatus, long?> value)
    {
        if (Volatile.Read(ref _chainStatus) is { } status && value(status) is { } measured)
        {
            yield return new Measurement<long>(measured);
        }
    }

    public void BecameStandby() => State = WorkerState.Standby;

    internal long LastAuditedRound => Interlocked.Read(ref _auditedRound);

    internal long AuditMismatches => Interlocked.Read(ref _auditMismatches);

    internal long AuditIgnored => Interlocked.Read(ref _auditIgnored);

    internal long AuditSkippedCount => Interlocked.Read(ref _auditSkipped);

    /// <summary>A round was compared with the ledger.</summary>
    /// <param name="round">The round.</param>
    public void ReportAudited(ulong round)
    {
        Interlocked.Exchange(ref _auditedRound, unchecked((long)round));
        Interlocked.Increment(ref _auditedRounds);
    }

    /// <summary>The database disagreed with the ledger.</summary>
    public void AuditMismatch() => Interlocked.Increment(ref _auditMismatches);

    /// <summary>The database disagreed at a round the operator listed in <c>IgnoreRounds</c>.</summary>
    public void AuditIgnoredMismatch() => Interlocked.Increment(ref _auditIgnored);

    /// <summary>A round or a pass could not be compared.</summary>
    public void AuditSkipped() => Interlocked.Increment(ref _auditSkipped);

    /// <summary>Progress of a partial rebuild; both zero when none runs.</summary>
    /// <param name="done">Ranges of the current stage that are finished.</param>
    /// <param name="total">Ranges of the current stage.</param>
    public void ReportRebuild(int done, int total)
    {
        // Both numbers change together, so a reader never sees more ranges done than there are.
        lock (_rebuildGate)
        {
            _rebuild = (done, total);
        }
    }

    public void BecameLeader()
    {
        Interlocked.Increment(ref _leasesAcquired);
        State = WorkerState.Leader;
    }

    public void LostLease()
    {
        Interlocked.Increment(ref _leasesLost);
        State = WorkerState.Standby;
    }

    public void ReportSampleFailure() => Interlocked.Increment(ref _sampleFailures);

    public void ReportTransientFailure() => Interlocked.Increment(ref _transientFailures);

    public void Dispose() => _meter.Dispose();
}
