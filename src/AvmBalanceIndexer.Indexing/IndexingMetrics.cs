using System.Diagnostics.Metrics;

namespace AvmBalanceIndexer.Indexing;

/// <summary>Why a block fetch from one host failed, as a metric tag.</summary>
public enum FetchFailure
{
    /// <summary>The host answered 429 (or a throttling 403/503) and was paused.</summary>
    Throttled,

    /// <summary>The host does not have the block.</summary>
    NotFound,

    /// <summary>The host served a block that failed validation.</summary>
    Invalid,

    /// <summary>Connection, timeout or HTTP error.</summary>
    Network,
}

/// <summary>What could not be decoded, as a metric tag.</summary>
public enum DecoderErrorKind
{
    /// <summary>A fetch failed because the nodes served blocks that failed validation and none served one that validates.</summary>
    Block,

    /// <summary>An application log starts with the ARC-200 transfer selector but is not a well-formed transfer event.</summary>
    Arc200Log,
}

/// <summary>How a holder scan ended, as a metric tag.</summary>
public enum ScanOutcome
{
    Completed,
    Failed,

    /// <summary>Cancelled, expired or taken over by another replica.</summary>
    Abandoned,
}

/// <summary>
/// Metrics of indexing, block fetching, decoding and holder scans on the meter <see cref="MeterName"/>. Tags have a few fixed values
/// only (no hosts, rounds or token ids). One instance per process; <see cref="None"/> for callers that do not export metrics.
/// </summary>
public sealed class IndexingMetrics : IDisposable
{
    public const string MeterName = "AvmBalanceIndexer.Indexing";

    /// <summary>
    /// Bucket boundaries in seconds, from a fast block download to a long holder scan. Without them the exporter would use its default
    /// boundaries, which are meant for milliseconds (0, 5, 10, 25, ...), and every percentile would be meaningless.
    /// </summary>
    public static IReadOnlyList<double> SecondsBucketBoundaries { get; } = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 1800];

    private static readonly InstrumentAdvice<double> SecondsBuckets = new() { HistogramBucketBoundaries = SecondsBucketBoundaries };

    private readonly Meter _meter;
    private readonly Histogram<double> _commitDuration;
    private readonly Counter<long> _commitEffects;
    private readonly Histogram<double> _fetchDuration;
    private readonly Counter<long> _fetchFailures;
    private readonly Counter<long> _decoderErrors;
    private readonly Histogram<double> _scanDuration;

    /// <param name="meterName">Only tests pass another name, to observe one instance without the others running in parallel.</param>
    public IndexingMetrics(string meterName = MeterName)
    {
        _meter = new Meter(meterName);
        _commitDuration = _meter.CreateHistogram("avm.indexer.commit.duration", "s", "Time to write and publish one batch of rounds", advice: SecondsBuckets);
        _commitEffects = _meter.CreateCounter<long>("avm.indexer.commit.effects", description: "Effects written with published rounds");
        _fetchDuration = _meter.CreateHistogram("avm.fetch.block.duration", "s", "Download and validation of a block that was served, by source (archival, relay); failed requests are in avm.fetch.block.failures", advice: SecondsBuckets);
        _fetchFailures = _meter.CreateCounter<long>("avm.fetch.block.failures", description: "Failed block requests to one host, by source and reason");
        _decoderErrors = _meter.CreateCounter<long>("avm.indexer.decoder_errors", description: "Fetches that failed because no node served a block that validates, and malformed ARC-200 transfer logs of committed rounds");

        // Both series exist from the start at 0, so that the first error is an increase that an alert (increase over a window) sees.
        _decoderErrors.Add(0, new KeyValuePair<string, object?>("kind", "block"));
        _decoderErrors.Add(0, new KeyValuePair<string, object?>("kind", "arc200_log"));
        _scanDuration = _meter.CreateHistogram("avm.scan.duration", "s", "Holder scans from claim to end, by outcome", advice: SecondsBuckets);
    }

    /// <summary>
    /// A shared instance for callers that are not wired to a metrics exporter. Its meter has a name of its own, so it never adds a second
    /// meter to the exported one; do not dispose it.
    /// </summary>
    public static IndexingMetrics None { get; } = new(MeterName + ".Unwired");

    /// <summary>A batch was published. Rounds are counted by <c>avm.worker.rounds_committed</c> already.</summary>
    public void CommitRecorded(TimeSpan duration, int effects)
    {
        _commitDuration.Record(duration.TotalSeconds);
        _commitEffects.Add(effects);
    }

    public void FetchSucceeded(TimeSpan duration, bool relay) =>
        _fetchDuration.Record(duration.TotalSeconds, Source(relay));

    public void FetchFailed(bool relay, FetchFailure reason) =>
        _fetchFailures.Add(1, Source(relay), new KeyValuePair<string, object?>("reason", reason switch
        {
            FetchFailure.Throttled => "throttled",
            FetchFailure.NotFound => "not_found",
            FetchFailure.Invalid => "invalid",
            _ => "network",
        }));

    public void DecoderError(DecoderErrorKind kind, int count = 1)
    {
        if (count > 0)
        {
            _decoderErrors.Add(count, new KeyValuePair<string, object?>("kind", kind == DecoderErrorKind.Block ? "block" : "arc200_log"));
        }
    }

    public void ScanEnded(TimeSpan duration, ScanOutcome outcome) =>
        _scanDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("outcome", outcome switch
        {
            ScanOutcome.Completed => "completed",
            ScanOutcome.Failed => "failed",
            _ => "abandoned",
        }));

    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?> Source(bool relay) => new("source", relay ? "relay" : "archival");
}
