using System.Diagnostics.Metrics;

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

    private readonly Meter _meter = new(MeterName);
    private int _state;
    private long _leasesAcquired;
    private long _leasesLost;
    private long _transientFailures;
    private Func<long> _roundsCommitted = static () => 0;

    public WorkerHealth()
    {
        _meter.CreateObservableGauge("avm.worker.is_leader", () => State == WorkerState.Leader ? 1 : 0, description: "1 while this replica holds the writer lease");
        _meter.CreateObservableCounter("avm.worker.leases_acquired", () => Interlocked.Read(ref _leasesAcquired), description: "Times this replica became the writer");
        _meter.CreateObservableCounter("avm.worker.leases_lost", () => Interlocked.Read(ref _leasesLost), description: "Times the writer lease was lost while indexing");
        _meter.CreateObservableCounter("avm.worker.transient_failures", () => Interlocked.Read(ref _transientFailures), description: "Retried indexing failures");
        _meter.CreateObservableCounter("avm.worker.rounds_committed", () => _roundsCommitted(), description: "Rounds this process committed");
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

    public void BecameStandby() => State = WorkerState.Standby;

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

    public void ReportTransientFailure() => Interlocked.Increment(ref _transientFailures);

    public void Dispose() => _meter.Dispose();
}
