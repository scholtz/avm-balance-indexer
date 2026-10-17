namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Condenses the rate limits of many nodes into few log lines. The first pause is reported at once, so the operator sees that
/// throttling has started; afterwards the pauses are collected and handed out at most once per interval.
/// </summary>
internal sealed class ThrottleReport(TimeSpan interval)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (int Pauses, int Limit)> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _since = DateTimeOffset.MinValue;
    private DateTimeOffset _lastReport = DateTimeOffset.MinValue;

    /// <summary>Hands out the pauses that were held back once the interval has passed, so they do not wait for the next pause to be seen.</summary>
    public ThrottleSummary? Flush(DateTimeOffset now)
    {
        lock (_lock)
        {
            return _hosts.Count > 0 && now - _lastReport >= interval ? TakeSummary(now) : null;
        }
    }

    /// <summary>Records a new pause of a node; returns the summary to log when one is due, otherwise null.</summary>
    public ThrottleSummary? Record(string host, int limit, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_hosts.Count == 0)
            {
                _since = now;
            }

            _hosts[host] = (_hosts.GetValueOrDefault(host).Pauses + 1, limit);
            if (_lastReport != DateTimeOffset.MinValue && now - _lastReport < interval)
            {
                return null;
            }

            return TakeSummary(now);
        }
    }

    private ThrottleSummary TakeSummary(DateTimeOffset now)
    {
        var summary = new ThrottleSummary(
            now - _since,
            [.. _hosts.Select(h => new HostThrottle(h.Key, h.Value.Pauses, h.Value.Limit)).OrderByDescending(h => h.Pauses).ThenBy(h => h.Host, StringComparer.Ordinal)]);
        _hosts.Clear();
        _lastReport = now;
        return summary;
    }
}

/// <param name="Window">The time the summary covers (zero for the first pause).</param>
/// <param name="Hosts">The nodes that paused, the busiest first.</param>
internal sealed record ThrottleSummary(TimeSpan Window, IReadOnlyList<HostThrottle> Hosts)
{
    public int Pauses { get; } = Hosts.Sum(h => h.Pauses);

    /// <summary>For the log: <c>a-6s (12 pauses, limit 6), a-4y (3 pauses, limit 8)</c>.</summary>
    public override string ToString() => string.Join(", ", Hosts.Select(h => $"{h.Host} ({h.Pauses} pause{(h.Pauses == 1 ? "" : "s")}, limit {h.Limit})"));
}

/// <param name="Host">The node.</param>
/// <param name="Pauses">How often it was paused in the window.</param>
/// <param name="Limit">Its concurrency limit after the last pause.</param>
internal readonly record struct HostThrottle(string Host, int Pauses, int Limit);
