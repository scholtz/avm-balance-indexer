using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Algorand.Gossip;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Fetches blocks from archival gossip nodes over HTTP (<c>/v1/{genesisId}/block/{round in base36}</c>), the same
/// protocol the Algorand5 <see cref="GossipHttpClient"/> speaks. Compared with calling that client directly it adds
/// status handling, host failover with cooldowns, per-attempt timeouts, bounded reads, exponential backoff with jitter,
/// and response validation, and it keeps the raw payload so ledger apply data (rewards, closing amounts) is not lost.
/// A node that rate-limits (429) is slowed down instead of being treated as broken: see <see cref="HostGate"/>.
/// </summary>
public sealed partial class GossipBlockFetcher : IBlockFetcher
{
    private readonly HttpClient _http;
    private readonly IGossipHostProvider _hosts;
    private readonly ChainProfile _profile;
    private readonly AlgorandChainOptions _options;
    private readonly byte[]? _expectedGenesisHash;
    private readonly ILogger<GossipBlockFetcher> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HostLatency> _latency = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HostGate> _gates = new(StringComparer.OrdinalIgnoreCase);

    public GossipBlockFetcher(
        HttpClient http,
        IGossipHostProvider hosts,
        ChainProfile profile,
        AlgorandChainOptions options,
        byte[]? expectedGenesisHash,
        ILogger<GossipBlockFetcher> logger,
        TimeProvider? timeProvider = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _expectedGenesisHash = expectedGenesisHash;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> discovered;
        try
        {
            discovered = await _hosts.GetHostsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new BlockFetchException("Archival gossip nodes could not be discovered.", round, ex);
        }

        if (discovered.Count == 0)
        {
            throw new BlockFetchException("No archival gossip nodes are available: none are configured, or DNS discovery is failing. Retrying.", round);
        }

        var hosts = OrderHosts(discovered);
        Exception? last = null;
        var notFound = 0;
        var errors = 0;
        var badData = 0;
        var throttling = new ThrottleClock();

        // A rate-limited answer is not an attempt: the node is paused and the request goes to the next one, or waits.
        for (var attempt = 0; attempt < _options.MaxAttempts;)
        {
            var (host, gate) = await AcquireHostAsync(hosts, attempt, round, throttling, cancellationToken).ConfigureAwait(false);
            try
            {
                var payload = await DownloadTimedAsync(host, round, cancellationToken).ConfigureAwait(false);
                gate.OnSuccess(_time.GetUtcNow());
                var block = BlockDecoder.Decode(payload, round, _profile, _expectedGenesisHash);
                _cooldownUntil.TryRemove(host, out _);
                return block;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HostThrottledException ex)
            {
                last = ex;
                var now = _time.GetUtcNow();
                var result = gate.OnThrottled(ex.RetryAfter, now);
                if (result.NewPause)
                {
                    LogHostThrottled(_logger, host, result.Delay, result.Limit);
                }

                throttling.Started ??= now;
                if (now - throttling.Started > _options.ThrottleTimeout)
                {
                    throw ThrottleTimedOut(round, last);
                }

                continue;
            }
            catch (BlockNotOnHostException ex)
            {
                last = ex;
                notFound++;
                LogNotOnHost(_logger, round, host, attempt + 1);
            }
            catch (Exception ex) when (ex is HttpRequestException or BlockValidationException or TimeoutException or IOException or UriFormatException)
            {
                last = ex;
                if (ex is BlockValidationException)
                {
                    badData++;
                }
                else
                {
                    errors++;
                }

                _cooldownUntil[host] = _time.GetUtcNow() + _options.HostCooldown;
                LogAttemptFailed(_logger, ex, round, host, attempt + 1, _options.MaxAttempts);
            }
            finally
            {
                gate.Exit();
            }

            if (++attempt < _options.MaxAttempts)
            {
                await Task.Delay(BackoffDelay(attempt - 1), _time, cancellationToken).ConfigureAwait(false);
            }
        }

        // Hosts saying "not here" at least as often as they fail to answer, and more often than they answer wrongly, means the
        // block is not out yet. Mostly errors or wrong answers means a broken fleet and must reach the caller as a failure.
        if (notFound > 0 && notFound >= errors && notFound > badData)
        {
            throw new BlockNotAvailableException(round);
        }

        throw new BlockFetchException($"Block {round} could not be fetched after {_options.MaxAttempts} attempts.", round, last);
    }

    /// <summary>When one fetch first met a rate limit; the time it may keep waiting for the nodes counts from here.</summary>
    private sealed class ThrottleClock
    {
        public DateTimeOffset? Started { get; set; }
    }

    private HostGate GateOf(string host) =>
        _gates.GetOrAdd(host, _ => new HostGate(_options.MaxConcurrentRequestsPerHost, _options.MaxThrottleDelay));

    private BlockFetchException ThrottleTimedOut(ulong round, Exception? last) =>
        new($"Block {round}: the archival gossip nodes kept rate limiting (HTTP 429) for more than {_options.ThrottleTimeout}. " +
            "Lower Indexer:FetchConcurrency or add archival nodes (Algorand:GossipHosts).", round, last);

    /// <summary>
    /// Takes a request slot at the first node, in the fetch's order starting at <paramref name="attempt"/>, that is neither paused
    /// nor full, so load spills over to the other nodes instead of queueing behind a busy one. When no node can take it the
    /// call waits: briefly while nodes are merely full, until the earliest pause ends while every node is paused (bounded by
    /// <see cref="AlgorandChainOptions.ThrottleTimeout"/> from the fetch's first rate limit).
    /// </summary>
    private async Task<(string Host, HostGate Gate)> AcquireHostAsync(string[] hosts, int attempt, ulong round, ThrottleClock throttling, CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = _time.GetUtcNow();
            var earliest = DateTimeOffset.MaxValue;
            var allPaused = true;
            for (var i = 0; i < hosts.Length; i++)
            {
                var host = hosts[(attempt + i) % hosts.Length];
                var gate = GateOf(host);
                if (gate.TryEnter(now))
                {
                    return (host, gate);
                }

                var until = gate.BlockedUntil;
                if (until > now)
                {
                    earliest = until < earliest ? until : earliest;
                }
                else
                {
                    allPaused = false;
                }
            }

            if (allPaused && throttling.Started is { } started && now - started > _options.ThrottleTimeout)
            {
                throw ThrottleTimedOut(round, null);
            }

            // Nodes that are only full free a slot within a request; paused ones come back at the end of their pause.
            var pause = allPaused ? earliest - now : TimeSpan.FromMilliseconds(10);
            await Task.Delay(pause < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : pause, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The hosts in the order a fetch tries them: healthy hosts first, picked at random with a weight of one over the square of
    /// their expected response time (so a host that answers in 50 ms gets about 100 times the requests of one that needs 500 ms
    /// and the requests in flight to it count against it), then the hosts that are cooling down. Attempt N of a fetch uses the
    /// Nth host, so every host is tried once before any is repeated.
    /// </summary>
    private string[] OrderHosts(IReadOnlyList<string> hosts)
    {
        var now = _time.GetUtcNow();
        var healthy = new List<(string Host, double Weight)>(hosts.Count);
        var cooling = new List<string>();
        foreach (var host in hosts)
        {
            if ((_cooldownUntil.TryGetValue(host, out var until) && until > now)
                || (_gates.TryGetValue(host, out var gate) && gate.BlockedUntil > now))
            {
                cooling.Add(host);
                continue;
            }

            var score = _latency.GetOrAdd(host, static _ => new HostLatency()).Score;
            healthy.Add((host, 1.0 / (score * score)));
        }

        var ordered = new List<string>(hosts.Count);
        var total = 0.0;
        foreach (var (_, weight) in healthy)
        {
            total += weight;
        }

        while (healthy.Count > 0)
        {
            var pick = Random.Shared.NextDouble() * total;
            var index = 0;
            for (; index < healthy.Count - 1; index++)
            {
                pick -= healthy[index].Weight;
                if (pick <= 0)
                {
                    break;
                }
            }

            ordered.Add(healthy[index].Host);
            total -= healthy[index].Weight;
            healthy.RemoveAt(index);
        }

        var offset = cooling.Count == 0 ? 0 : Random.Shared.Next(cooling.Count);
        ordered.AddRange(cooling.Skip(offset).Concat(cooling.Take(offset)));
        return [.. ordered];
    }

    private enum HostOutcome
    {
        /// <summary>The host delivered the bytes.</summary>
        Answered,

        /// <summary>The request failed, timed out, or the host said it does not have the block.</summary>
        Failed,

        /// <summary>The caller gave up; nothing is learned.</summary>
        Ignored,
    }

    /// <summary>Smoothed response time of one host and the number of requests currently waiting for it.</summary>
    private sealed class HostLatency
    {
        // Unknown hosts start in the middle of what public archival nodes deliver, so they are tried soon.
        private const double InitialMilliseconds = 250;
        private const double Smoothing = 0.2;
        private const double PenaltyMilliseconds = 2_000;
        private readonly object _gate = new();
        private double _milliseconds = InitialMilliseconds;
        private long _updated = Stopwatch.GetTimestamp();
        private int _inFlight;

        /// <summary>
        /// Expected response time scaled by the queue in front of it. What was learned fades back to the initial guess within
        /// minutes, so a host that was slow (or lacked blocks of one range) is tried again later.
        /// </summary>
        public double Score
        {
            get
            {
                lock (_gate)
                {
                    var idle = Stopwatch.GetElapsedTime(_updated).TotalSeconds;
                    var learned = InitialMilliseconds + ((_milliseconds - InitialMilliseconds) * Math.Exp(-idle / 120));
                    return Math.Max(1, learned) * (1 + (0.25 * _inFlight));
                }
            }
        }

        public void Begin()
        {
            lock (_gate)
            {
                _inFlight++;
            }
        }

        public void End(TimeSpan elapsed, HostOutcome outcome)
        {
            lock (_gate)
            {
                _inFlight--;
                if (outcome == HostOutcome.Ignored)
                {
                    return;
                }

                var sample = outcome == HostOutcome.Answered ? elapsed.TotalMilliseconds : Math.Max(PenaltyMilliseconds, elapsed.TotalMilliseconds);
                _milliseconds = ((1 - Smoothing) * _milliseconds) + (Smoothing * sample);
                _updated = Stopwatch.GetTimestamp();
            }
        }
    }

    private TimeSpan BackoffDelay(int attempt)
    {
        var ceiling = Math.Min(_options.MaxRetryDelay.TotalMilliseconds, _options.BaseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
    }

    /// <summary>Downloads a block and records how long the host took (transport only, not decoding); cancellation says nothing about the host.</summary>
    private async Task<byte[]> DownloadTimedAsync(string host, ulong round, CancellationToken cancellationToken)
    {
        var latency = _latency.GetOrAdd(host, static _ => new HostLatency());
        latency.Begin();
        var started = Stopwatch.GetTimestamp();
        var outcome = HostOutcome.Failed;
        try
        {
            var payload = await DownloadAsync(host, round, cancellationToken).ConfigureAwait(false);
            outcome = HostOutcome.Answered;
            return payload;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = HostOutcome.Ignored;
            throw;
        }
        finally
        {
            latency.End(Stopwatch.GetElapsedTime(started), outcome);
        }
    }

    private async Task<byte[]> DownloadAsync(string host, ulong round, CancellationToken cancellationToken)
    {
        var url = $"{host.TrimEnd('/')}/v1/{_profile.GenesisId}/block/{GossipHttpClient.ToBase36(round)}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        try
        {
            using var response = await _http.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BlockNotOnHostException(host, round);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests
                || (response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null))
            {
                throw new HostThrottledException(host, round, RetryAfterOf(response));
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > _options.MaxBlockBytes)
            {
                throw new HttpRequestException($"Block {round} from {host} exceeds {_options.MaxBlockBytes} bytes.");
            }

            return await ReadBoundedAsync(response.Content, host, round, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Fetching block {round} from {host} timed out after {_options.RequestTimeout}.");
        }
    }

    /// <summary>The node's <c>Retry-After</c> (seconds or HTTP date) as a delay from now; null when it sent none.</summary>
    private TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - _time.GetUtcNow() : null;
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, string host, ulong round, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream((int)Math.Min(content.Headers.ContentLength ?? 262_144, _options.MaxBlockBytes));
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _options.MaxBlockBytes)
            {
                throw new HttpRequestException($"Block {round} from {host} exceeds {_options.MaxBlockBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private sealed class BlockNotOnHostException(string host, ulong round)
        : Exception($"Node {host} does not have block {round}.");

    private sealed class HostThrottledException(string host, ulong round, TimeSpan? retryAfter)
        : Exception($"Node {host} is rate limiting (fetching block {round}).")
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    [LoggerMessage(EventId = 1110, Level = LogLevel.Debug, Message = "Block {Round} not on {Host} (attempt {Attempt})")]
    private static partial void LogNotOnHost(ILogger logger, ulong round, string host, int attempt);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Information, Message = "Archival node {Host} is rate limiting (429); pausing it for {Delay} and allowing {Limit} concurrent requests")]
    private static partial void LogHostThrottled(ILogger logger, string host, TimeSpan delay, int limit);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "Fetching block {Round} from {Host} failed (attempt {Attempt}/{MaxAttempts})")]
    private static partial void LogAttemptFailed(ILogger logger, Exception exception, ulong round, string host, int attempt, int maxAttempts);
}
