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
    private string[] _hosts = [];
    private DateTimeOffset _expires = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken)
    {
        if (_time.GetUtcNow() < _expires && _hosts.Length > 0)
        {
            return _hosts;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_time.GetUtcNow() < _expires && _hosts.Length > 0)
            {
                return _hosts;
            }

            try
            {
                // The SDK resolver is synchronous (raw UDP), so keep it off the caller thread.
                var configuration = await Task.Run(
                    () => new GossipHttpConfiguration(GossipNodePurpose.Archival, network),
                    cancellationToken).ConfigureAwait(false);
                _hosts = configuration.Hosts;
                _expires = _time.GetUtcNow() + refreshInterval;
                LogDiscovered(logger, _hosts.Length, network);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && _hosts.Length > 0)
            {
                _expires = _time.GetUtcNow() + TimeSpan.FromSeconds(30);
                LogRefreshFailed(logger, ex, network, _hosts.Length);
            }

            return _hosts;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Discovered {Count} archival gossip nodes for {Network}")]
    private static partial void LogDiscovered(ILogger logger, int count, GossipNetwork network);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "DNS SRV refresh for {Network} failed; keeping {Count} previously discovered nodes")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, GossipNetwork network, int count);
}
