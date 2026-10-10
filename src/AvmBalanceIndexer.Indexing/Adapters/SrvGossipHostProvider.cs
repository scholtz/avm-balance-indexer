using Algorand.Gossip;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Discovers archival gossip nodes through the network DNS SRV record (via the Algorand5 resolver) and caches the
/// result. If a refresh fails the last known good list keeps being served.
/// </summary>
public sealed partial class SrvGossipHostProvider(
    GossipNetwork network,
    TimeSpan refreshInterval,
    ILogger<SrvGossipHostProvider> logger,
    TimeProvider? timeProvider = null) : IGossipHostProvider, IDisposable
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Snapshot? _snapshot;

    public async Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is { } fresh && _time.GetUtcNow() < fresh.Expires)
        {
            return fresh.Hosts;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot is { } current && _time.GetUtcNow() < current.Expires)
            {
                return current.Hosts;
            }

            try
            {
                // The SDK resolver is synchronous (raw UDP), so keep it off the caller thread.
                var configuration = await Task.Run(
                    () => new GossipHttpConfiguration(GossipNodePurpose.Archival, network),
                    cancellationToken).ConfigureAwait(false);
                _snapshot = new Snapshot(configuration.Hosts, _time.GetUtcNow() + refreshInterval);
                LogDiscovered(logger, configuration.Hosts.Length, network);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && _snapshot is { Hosts.Length: > 0 } stale)
            {
                // Keep serving the last known hosts and look again soon.
                _snapshot = stale with { Expires = _time.GetUtcNow() + TimeSpan.FromSeconds(30) };
                LogRefreshFailed(logger, ex, network, stale.Hosts.Length);
            }

            return _snapshot?.Hosts ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record Snapshot(string[] Hosts, DateTimeOffset Expires);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Discovered {Count} archival gossip nodes for {Network}")]
    private static partial void LogDiscovered(ILogger logger, int count, GossipNetwork network);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "DNS SRV refresh for {Network} failed; keeping {Count} previously discovered nodes")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, GossipNetwork network, int count);
}
