namespace AvmBalanceIndexer.Worker;

/// <summary>What the writer does after one quota answer of the node.</summary>
/// <param name="Wait">How long to wait before the next attempt.</param>
/// <param name="Log">Whether to log the answer now (the first one, then at most every <see cref="QuotaWait.LogInterval"/>).</param>
/// <param name="GiveUp">The node has refused for so long without any progress that it is not a daily quota: a failure.</param>
internal readonly record struct QuotaStep(TimeSpan Wait, bool Log, bool GiveUp);

/// <summary>
/// The waits of the writer while a public node answers that its daily quota is used up. Near the quota only a share of the requests is
/// refused, so the wait starts short; it doubles up to the longest wait while nothing is indexed between two refusals (every refused
/// request counts against the quota too). A daily quota is renewed within a day: a node that still refuses after
/// <see cref="GiveUpAfter"/> without progress has another problem (a plan without quota, a monthly limit) and the writer stops.
/// </summary>
internal sealed class QuotaWait(TimeSpan first, TimeSpan longest, TimeProvider time)
{
    public static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(26);

    private readonly TimeSpan _first = first < longest ? first : longest;
    private TimeSpan _next = first < longest ? first : longest;
    private long? _progress;
    private long _refusedSince;
    private long? _loggedAt;

    /// <summary>The step after a quota answer.</summary>
    /// <param name="progress">A number that changes whenever the writer made progress (rounds committed).</param>
    public QuotaStep Next(long progress)
    {
        var now = time.GetTimestamp();
        if (_progress != progress)
        {
            _progress = progress;
            _next = _first;
            _refusedSince = now;
        }

        var wait = _next;
        _next = TimeSpan.FromTicks(Math.Min(_next.Ticks * 2, longest.Ticks));

        var log = _loggedAt is not { } loggedAt || time.GetElapsedTime(loggedAt, now) >= LogInterval;
        if (log)
        {
            _loggedAt = now;
        }

        return new QuotaStep(wait, log, time.GetElapsedTime(_refusedSince, now) >= GiveUpAfter);
    }
}
