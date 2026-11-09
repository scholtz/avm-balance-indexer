using Algorand.Gossip;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Discovers archival gossip nodes and relays through the network DNS SRV records (via the Algorand5 resolver) and caches the
/// results. If a refresh fails the last known good list keeps being served. Relays are optional: when none can be found the
/// archival nodes carry all requests.
/// </summary>
public sealed partial class SrvGossipHostProvider(
    GossipNetwork network,
    TimeSpan refreshInterval,
    ILogger<SrvGossipHostProvider> logger,
    TimeProvider? timeProvider = null,
    Func<GossipNodePurpose, GossipNetwork, string[]>? resolver = null) : IGossipHostProvider, IDisposable
{
    private static readonly TimeSpan RetryStaleAfterFailure = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryEmptyAfterFailure = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryRelaysAfterFailure = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<GossipNodePurpose, GossipNetwork, string[]> _resolve = resolver ?? ResolveWithSdk;
    private readonly HostList _archival = new(GossipNodePurpose.Archival, RetryEmptyAfterFailure, RetryStaleAfterFailure);
    // A network may have no relay record at all, so a failed lookup is not repeated every few seconds.
    private readonly HostList _relays = new(GossipNodePurpose.Relay, RelayRetry(refreshInterval), RelayRetry(refreshInterval));

    private static TimeSpan RelayRetry(TimeSpan refreshInterval) => refreshInterval < RetryRelaysAfterFailure ? refreshInterval : RetryRelaysAfterFailure;

    private static string[] ResolveWithSdk(GossipNodePurpose purpose, GossipNetwork network) =>
        new GossipHttpConfiguration(purpose, network).Hosts;

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
            LogRelaysUnavailable(logger, ex, network);
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> GetAsync(HostList directory, CancellationToken cancellationToken)
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
                var hosts = await Task.Run(() => _resolve(directory.Purpose, network), cancellationToken).ConfigureAwait(false);
                if (hosts.Length == 0)
                {
                    throw new InvalidOperationException($"DNS SRV discovery returned no {directory.Name} nodes.");
                }

                directory.Snapshot = new Snapshot(hosts, _time.GetUtcNow() + refreshInterval);
                LogDiscovered(logger, hosts.Length, directory.Name, network);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (directory.Snapshot is { Hosts.Length: > 0 } stale)
                {
                    // Keep serving the last known hosts and look again soon.
                    directory.Snapshot = stale with { Expires = _time.GetUtcNow() + directory.RetryStaleAfter };
                    LogRefreshFailed(logger, ex, directory.Name, network, stale.Hosts.Length);
                }
                else
                {
                    // Nothing to fall back on. Remember the failure briefly so that concurrent fetches do not each repeat the
                    // blocking lookup; they see no hosts and fail fast, and the next lookup happens once the pause is over.
                    directory.Snapshot = new Snapshot([], _time.GetUtcNow() + directory.RetryEmptyAfter);
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

    private sealed class HostList(GossipNodePurpose purpose, TimeSpan retryEmptyAfter, TimeSpan retryStaleAfter)
    {
        public GossipNodePurpose Purpose { get; } = purpose;

        public TimeSpan RetryEmptyAfter { get; } = retryEmptyAfter;

        public TimeSpan RetryStaleAfter { get; } = retryStaleAfter;

        public string Name { get; } = purpose == GossipNodePurpose.Relay ? "relay" : "archival";

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public volatile Snapshot? Snapshot;
    }

    private sealed record Snapshot(string[] Hosts, DateTimeOffset Expires);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Discovered {Count} {Kind} gossip nodes for {Network}")]
    private static partial void LogDiscovered(ILogger logger, int count, string kind, GossipNetwork network);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "DNS SRV refresh of {Kind} nodes for {Network} failed; keeping {Count} previously discovered nodes")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, string kind, GossipNetwork network, int count);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "No relays could be discovered for {Network}; recent blocks are fetched from archival nodes only")]
    private static partial void LogRelaysUnavailable(ILogger logger, Exception exception, GossipNetwork network);
}
