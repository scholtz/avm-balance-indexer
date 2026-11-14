using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Indexing.Tests;

public sealed class IndexingMetricsTests : IDisposable
{
    private readonly MetricsCapture _capture = new();

    public void Dispose() => _capture.Dispose();

    [Fact]
    public void A_Commit_Records_Its_Duration_And_Effects()
    {
        _capture.Metrics.CommitRecorded(TimeSpan.FromMilliseconds(250), effects: 40);

        Assert.Equal(0.25, Assert.Single(_capture.Of("avm.indexer.commit.duration")).Value, 6);
        Assert.Equal(40, Assert.Single(_capture.Of("avm.indexer.commit.effects")).Value);
    }

    [Theory]
    [InlineData(true, "relay")]
    [InlineData(false, "archival")]
    public void A_Fetch_Records_Its_Duration_By_Source(bool relay, string source)
    {
        _capture.Metrics.FetchSucceeded(TimeSpan.FromMilliseconds(80), relay);

        var measurement = Assert.Single(_capture.Of("avm.fetch.block.duration"));
        Assert.Equal(0.08, measurement.Value, 6);
        Assert.Equal(source, measurement.Tags["source"]);
    }

    [Theory]
    [InlineData(FetchFailure.Throttled, "throttled")]
    [InlineData(FetchFailure.NotFound, "not_found")]
    [InlineData(FetchFailure.Invalid, "invalid")]
    [InlineData(FetchFailure.Network, "network")]
    public void A_Failed_Fetch_Counts_By_Source_And_Reason(FetchFailure failure, string reason)
    {
        _capture.Metrics.FetchFailed(relay: true, failure);

        Assert.Equal(1, _capture.Sum("avm.fetch.block.failures", ("source", "relay"), ("reason", reason)));
    }

    [Fact]
    public void Decoder_Errors_Count_By_Kind_And_Nothing_For_Zero()
    {
        _capture.Metrics.DecoderError(DecoderErrorKind.Block);
        _capture.Metrics.DecoderError(DecoderErrorKind.Arc200Log, 2);
        _capture.Metrics.DecoderError(DecoderErrorKind.Arc200Log, 0);

        Assert.Equal([("block", 1d), ("arc200_log", 2d)], _capture.Of("avm.indexer.decoder_errors").Select(static m => ((string)m.Tags["kind"]!, m.Value)));
    }

    [Theory]
    [InlineData(ScanOutcome.Completed, "completed")]
    [InlineData(ScanOutcome.Failed, "failed")]
    [InlineData(ScanOutcome.Abandoned, "abandoned")]
    public void A_Scan_Records_Its_Duration_By_Outcome(ScanOutcome outcome, string tag)
    {
        _capture.Metrics.ScanEnded(TimeSpan.FromSeconds(3), outcome);

        var measurement = Assert.Single(_capture.Of("avm.scan.duration"));
        Assert.Equal(3, measurement.Value, 6);
        Assert.Equal(tag, measurement.Tags["outcome"]);
    }

    [Theory]
    [InlineData("avm.indexer.commit.duration")]
    [InlineData("avm.fetch.block.duration")]
    [InlineData("avm.scan.duration")]
    public void Duration_Histograms_Advise_Second_Scale_Buckets(string name)
    {
        // The exporter's defaults are for milliseconds (0, 5, 10, ...): with them, a 50 ms fetch and a 4 s commit share one bucket.
        System.Diagnostics.Metrics.Histogram<double>? histogram = null;
        using var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Name == name && instrument is System.Diagnostics.Metrics.Histogram<double> found && ReferenceEquals(found.Meter, MeterOf(_capture.Metrics)))
                {
                    histogram = found;
                }
            },
        };
        listener.Start();

        Assert.NotNull(histogram);
        Assert.Equal(IndexingMetrics.SecondsBucketBoundaries, histogram.Advice?.HistogramBucketBoundaries);
        Assert.Contains(0.05, histogram.Advice!.HistogramBucketBoundaries!);
    }

    private static System.Diagnostics.Metrics.Meter? MeterOf(IndexingMetrics metrics) =>
        typeof(IndexingMetrics).GetField("_meter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(metrics) as System.Diagnostics.Metrics.Meter;

    [Fact]
    public void The_Unwired_Instance_Is_Not_On_The_Exported_Meter()
    {
        var unwired = MeterOf(IndexingMetrics.None);

        Assert.NotNull(unwired);
        Assert.NotEqual(IndexingMetrics.MeterName, unwired.Name);
    }
}
