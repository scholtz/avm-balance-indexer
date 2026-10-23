using Algorand.Gossip;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Discovers archival gossip nodes and relays through the network DNS SRV records (via the Algorand5 resolver) and caches the
/// results. If a refresh fails the last known good list keeps being served. Relays are optional: when none can be found the
/// archival nodes carry all requests.
/// </summary>
public sealed partial class SrvGossipHostProvider : IGossipHostProvider, IDisposable
{
    private static readonly TimeSpan RetryStaleAfterFailure = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryEmptyAfterFailure = TimeSpan.FromSeconds(5);
    private readonly GossipNetwork _network;
    private readonly TimeSpan _refreshInterval;
    private readonly ILogger<SrvGossipHostProvider> _logger;
    private readonly TimeProvider _time;
    private readonly Directory _archival = new(GossipNodePurpose.Archival);
    private readonly Directory _relays = new(GossipNodePurpose.Relay);

    public SrvGossipHostProvider(
        GossipNetwork network,
        TimeSpan refreshInterval,
        ILogger<SrvGossipHostProvider> logger,
        TimeProvider? timeProvider = null)
    {
        _network = network;
        _refreshInterval = refreshInterval;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken) =>
        GetAsync(_archival, cancellationToken);

    public async Task<IReadOnlyList<string>> GetRelayHostsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync(_relays, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Relays only add capacity near the tip; the archival nodes still serve every block without them.
            LogRelaysUnavailable(_logger, ex, _network);
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> GetAsync(Directory directory, CancellationToken cancellationToken)
    {
        if (directory.Snapshot is { } fresh && _time.GetUtcNow() < fresh.Expires)
        {
            return fresh.Hosts;
        }

        try
        {
            await directory.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The adapter is being shut down while a fetch was still asking for hosts: that is a cancellation, not a failure.
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            if (directory.Snapshot is { } current && _time.GetUtcNow() < current.Expires)
            {
                return current.Hosts;
            }

            try
            {
                // The SDK resolver is synchronous (raw UDP), so keep it off the caller thread.
                var configuration = await Task.Run(
                    () => new GossipHttpConfiguration(directory.Purpose, _network),
                    cancellationToken).ConfigureAwait(false);
                if (configuration.Hosts.Length == 0)
                {
                    throw new InvalidOperationException($"DNS SRV discovery returned no {directory.Name} nodes.");
                }

                directory.Snapshot = new Snapshot(configuration.Hosts, _time.GetUtcNow() + _refreshInterval);
                LogDiscovered(_logger, configuration.Hosts.Length, directory.Name, _network);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (directory.Snapshot is { Hosts.Length: > 0 } stale)
                {
                    // Keep serving the last known hosts and look again soon.
                    directory.Snapshot = stale with { Expires = _time.GetUtcNow() + RetryStaleAfterFailure };
                    LogRefreshFailed(_logger, ex, directory.Name, _network, stale.Hosts.Length);
                }
                else
                {
                    // Nothing to fall back on. Remember the failure briefly so that concurrent fetches do not each repeat the
                    // blocking lookup; they see no hosts and fail fast, and the next lookup happens once the pause is over.
                    directory.Snapshot = new Snapshot([], _time.GetUtcNow() + RetryEmptyAfterFailure);
                    throw;
                }
            }

            return directory.Snapshot?.Hosts ?? [];
        }
        finally
        {
            try
            {
                directory.Gate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while a lookup was running; nothing left to release.
            }
        }
    }

    public void Dispose()
    {
        _archival.Gate.Dispose();
        _relays.Gate.Dispose();
    }

    private sealed class Directory(GossipNodePurpose purpose)
    {
        public GossipNodePurpose Purpose { get; } = purpose;

        public string Name { get; } = purpose == GossipNodePurpose.Relay ? "relay" : "archival";

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public volatile Snapshot? Snapshot;
    }

    private sealed record Snapshot(string[] Hosts, DateTimeOffset Expires);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Discovered {Count} {Kind} gossip nodes for {Network}")]
    private static partial void LogDiscovered(ILogger logger, int count, string kind, GossipNetwork network);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "DNS SRV refresh of {Kind} nodes for {Network} failed; keeping {Count} previously discovered nodes")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, string kind, GossipNetwork network, int count);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "No relays could be discovered for {Network}; recent blocks are fetched from archival nodes only")]
    private static partial void LogRelaysUnavailable(ILogger logger, Exception exception, GossipNetwork network);
}
