using System.Collections.Concurrent;
using System.Net;
using Algorand.Gossip;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Fetches blocks from archival gossip nodes over HTTP (<c>/v1/{genesisId}/block/{round in base36}</c>), the same
/// protocol the Algorand5 <see cref="GossipHttpClient"/> speaks. Compared with calling that client directly it adds
/// status handling, host failover with cooldowns, per-attempt timeouts, bounded reads, exponential backoff with jitter,
/// and response validation, and it keeps the raw payload so ledger apply data (rewards, closing amounts) is not lost.
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
            throw new BlockFetchException("No archival gossip nodes are configured or discoverable.", round);
        }

        var hosts = OrderHosts(discovered);
        Exception? last = null;
        var notFound = 0;
        var errors = 0;
        var badData = 0;

        for (var attempt = 0; attempt < _options.MaxAttempts; attempt++)
        {
            var host = hosts[attempt % hosts.Length];
            try
            {
                var payload = await DownloadAsync(host, round, cancellationToken).ConfigureAwait(false);
                var block = BlockDecoder.Decode(payload, round, _profile, _expectedGenesisHash);
                _cooldownUntil.TryRemove(host, out _);
                return block;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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

            if (attempt + 1 < _options.MaxAttempts)
            {
                await Task.Delay(BackoffDelay(attempt), _time, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// A random rotation of the hosts with the ones that are not cooling down first. Attempt N of a fetch uses the
    /// Nth host, so every host is tried once before any is repeated.
    /// </summary>
    private string[] OrderHosts(IReadOnlyList<string> hosts)
    {
        var now = _time.GetUtcNow();
        var start = Random.Shared.Next(hosts.Count);
        var rotated = Enumerable.Range(0, hosts.Count).Select(i => hosts[(start + i) % hosts.Count]).ToArray();
        bool Healthy(string host) => !_cooldownUntil.TryGetValue(host, out var until) || until <= now;
        return [.. rotated.Where(Healthy), .. rotated.Where(h => !Healthy(h))];
    }

    private TimeSpan BackoffDelay(int attempt)
    {
        var ceiling = Math.Min(_options.MaxRetryDelay.TotalMilliseconds, _options.BaseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
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

    [LoggerMessage(EventId = 1110, Level = LogLevel.Debug, Message = "Block {Round} not on {Host} (attempt {Attempt})")]
    private static partial void LogNotOnHost(ILogger logger, ulong round, string host, int attempt);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "Fetching block {Round} from {Host} failed (attempt {Attempt}/{MaxAttempts})")]
    private static partial void LogAttemptFailed(ILogger logger, Exception exception, ulong round, string host, int attempt, int maxAttempts);
}
