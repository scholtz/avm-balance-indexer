using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AvmBalanceIndexer.Indexing;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// An <see cref="IndexingMetrics"/> on a meter name of its own with a listener that records every measurement. The unique name matters:
/// a MeterListener sees every meter of a name, and test classes run in parallel.
/// </summary>
public sealed class MetricsCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Name, double Value, IReadOnlyDictionary<string, object?> Tags)> _measurements = new();

    public MetricsCapture()
    {
        var name = $"test.{Guid.NewGuid():N}";
        Metrics = new IndexingMetrics(name);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public IndexingMetrics Metrics { get; }

    /// <summary>The measurements of one instrument, in the order they were made.</summary>
    public IReadOnlyList<(double Value, IReadOnlyDictionary<string, object?> Tags)> Of(string instrument) =>
        [.. _measurements.Where(m => m.Name == instrument).Select(static m => (m.Value, m.Tags))];

    /// <summary>The sum of one instrument's measurements whose tags include all of <paramref name="tags"/>.</summary>
    public double Sum(string instrument, params (string Key, string Value)[] tags) =>
        Of(instrument).Where(m => tags.All(t => m.Tags.TryGetValue(t.Key, out var v) && (string?)v == t.Value)).Sum(static m => m.Value);

    public void Dispose()
    {
        _listener.Dispose();
        Metrics.Dispose();
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
}
