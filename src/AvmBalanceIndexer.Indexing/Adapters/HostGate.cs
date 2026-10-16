namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Admission control for one archival gossip node. At most <see cref="Limit"/> requests are in flight; the limit follows the
/// node's own tolerance (additive increase, multiplicative decrease): it halves when the node answers with a rate limit and
/// grows back by about one request per limit's worth of successes. A rate limit also pauses the node, for the
/// <c>Retry-After</c> it sent or, without one, for a back-off that doubles with every pause in a row.
/// A response belongs to the earlier episode when its request was admitted before the pause ended: it neither halves the limit
/// again nor lengthens the back-off, so a burst of simultaneous 429s costs the node one step, not one step per request. A node
/// that keeps answering 429 is given more time with every pause even when it asks for little (<c>Retry-After: 0</c>).
/// </summary>
internal sealed class HostGate(int maxConcurrency, TimeSpan maxDelay)
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);

    /// <summary>A pause never lasts less than this, so a <c>Retry-After: 0</c> cannot turn the retry loop into a spin.</summary>
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(5);

    private readonly object _lock = new();
    private double _limit = maxConcurrency;
    private int _inFlight;
    private int _pausesInARow;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    /// <summary>How many requests may be in flight now.</summary>
    public int Limit
    {
        get
        {
            lock (_lock)
            {
                return (int)Math.Floor(_limit);
            }
        }
    }

    /// <summary>The node is not asked before this moment (in the past or <see cref="DateTimeOffset.MinValue"/> when it is not paused).</summary>
    public DateTimeOffset BlockedUntil
    {
        get
        {
            lock (_lock)
            {
                return _blockedUntil;
            }
        }
    }

    /// <summary>Takes a slot when the node is not paused and has capacity; every success must be paired with <see cref="Exit"/>.</summary>
    public bool TryEnter(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (now < _blockedUntil || _inFlight >= (int)Math.Floor(_limit))
            {
                return false;
            }

            _inFlight++;
            return true;
        }
    }

    public void Exit()
    {
        lock (_lock)
        {
            _inFlight--;
        }
    }

    /// <summary>The node delivered a block (or an ordinary answer): it can take a little more.</summary>
    /// <param name="entered">When the request was admitted (<see cref="TryEnter"/>).</param>
    public void OnSuccess(DateTimeOffset entered)
    {
        lock (_lock)
        {
            if (entered < _blockedUntil)
            {
                return; // admitted before the last pause ended: says nothing about the node's tolerance now
            }

            _pausesInARow = 0;
            _limit = Math.Min(maxConcurrency, _limit + (1.0 / _limit));
        }
    }

    /// <summary>The node rate-limited a request.</summary>
    /// <param name="retryAfter">The node's <c>Retry-After</c>, when it sent one.</param>
    /// <param name="entered">When the request that was answered was admitted (<see cref="TryEnter"/>).</param>
    /// <param name="now">The current time.</param>
    /// <returns>Whether this started a new pause, the length of the pause now in force, and the new limit.</returns>
    public ThrottleResult OnThrottled(TimeSpan? retryAfter, DateTimeOffset entered, DateTimeOffset now)
    {
        lock (_lock)
        {
            var newEpisode = entered >= _blockedUntil;
            if (newEpisode)
            {
                _pausesInARow++;
                _limit = Math.Max(1, _limit / 2);
            }

            var backoff = TimeSpan.FromTicks(FirstDelay.Ticks << Math.Min(_pausesInARow - 1, 20));
            var given = retryAfter is { } value && value > TimeSpan.Zero ? value : TimeSpan.Zero;
            var delay = retryAfter is null || _pausesInARow > 1 ? Max(given, backoff) : given;
            delay = Min(delay, maxDelay);
            delay = delay < MinimumDelay ? MinimumDelay : delay;

            if (newEpisode || (retryAfter is not null && now + delay > _blockedUntil))
            {
                _blockedUntil = now + delay;
            }

            return new ThrottleResult(newEpisode, _blockedUntil - now, (int)Math.Floor(_limit));
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}

/// <summary>What a rate limit did to a node.</summary>
/// <param name="NewPause">The response started a pause; false for a late response of one that was already running.</param>
/// <param name="Delay">How long the node is paused from now.</param>
/// <param name="Limit">The node's concurrency limit after the response.</param>
internal readonly record struct ThrottleResult(bool NewPause, TimeSpan Delay, int Limit);
