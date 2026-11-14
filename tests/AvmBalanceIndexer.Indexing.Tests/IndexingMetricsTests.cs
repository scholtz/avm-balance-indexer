using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace AvmBalanceIndexer.Indexing.Tests;

public sealed class IndexingMetricsTests : IDisposable
{
    // A unique meter name per test: a MeterListener sees every meter of a name, and test classes run in parallel.
    private readonly string _meterName = $"test.{Guid.NewGuid():N}";
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = new();

    public IndexingMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == _meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            map[tag.Key] = tag.Value;
        }

        _measurements.Enqueue((instrument.Name, value, map));
    }

    private List<(string Name, double Value, Dictionary<string, object?> Tags)> Of(string name) => [.. _measurements.Where(m => m.Name == name)];

    [Fact]
    public void A_Commit_Records_Its_Duration_Rounds_And_Effects()
    {
        using var metrics = new IndexingMetrics(_meterName);

        metrics.CommitRecorded(TimeSpan.FromMilliseconds(250), rounds: 3, effects: 40);

        Assert.Equal(0.25, Assert.Single(Of("avm.indexer.commit.duration")).Value, 6);
        Assert.Equal(3, Assert.Single(Of("avm.indexer.commit.rounds")).Value);
        Assert.Equal(40, Assert.Single(Of("avm.indexer.commit.effects")).Value);
    }

    [Theory]
    [InlineData(true, "relay")]
    [InlineData(false, "archival")]
    public void A_Fetch_Records_Its_Duration_By_Source(bool relay, string source)
    {
        using var metrics = new IndexingMetrics(_meterName);

        metrics.FetchSucceeded(TimeSpan.FromMilliseconds(80), relay);

        var measurement = Assert.Single(Of("avm.fetch.block.duration"));
        Assert.Equal(0.08, measurement.Value, 6);
        Assert.Equal(source, measurement.Tags["source"]);
    }

    [Fact]
    public void A_Failed_Fetch_Counts_By_Source_And_Reason()
    {
        using var metrics = new IndexingMetrics(_meterName);

        metrics.FetchFailed(relay: true, FetchFailure.Invalid);
        metrics.FetchFailed(relay: false, FetchFailure.NotFound);

        var failures = Of("avm.fetch.block.failures");
        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, static f => (string?)f.Tags["source"] == "relay" && (string?)f.Tags["reason"] == "invalid");
        Assert.Contains(failures, static f => (string?)f.Tags["source"] == "archival" && (string?)f.Tags["reason"] == "not_found");
    }

    [Fact]
    public void Decoder_Errors_And_Round_Failures_Count_By_Kind()
    {
        using var metrics = new IndexingMetrics(_meterName);

        metrics.DecoderError(DecoderErrorKind.Block);
        metrics.DecoderError(DecoderErrorKind.Arc200Log, 2);
        metrics.RoundFailed(transient: true);
        metrics.RoundFailed(transient: false);

        Assert.Equal([("block", 1d), ("arc200_log", 2d)], Of("avm.indexer.decoder_errors").Select(static m => ((string)m.Tags["kind"]!, m.Value)));
        Assert.Equal(["transient", "fatal"], Of("avm.indexer.round_failures").Select(static m => (string)m.Tags["reason"]!));
    }

    [Fact]
    public void A_Scan_Records_Its_Duration_By_Outcome()
    {
        using var metrics = new IndexingMetrics(_meterName);

        metrics.ScanEnded(TimeSpan.FromSeconds(3), ScanOutcome.Completed);

        var measurement = Assert.Single(Of("avm.scan.duration"));
        Assert.Equal(3, measurement.Value, 6);
        Assert.Equal("completed", measurement.Tags["outcome"]);
    }

    [Fact]
    public void The_Shared_Instance_Without_Listeners_Accepts_Measurements() =>
        IndexingMetrics.None.CommitRecorded(TimeSpan.FromSeconds(1), 1, 1);
}
