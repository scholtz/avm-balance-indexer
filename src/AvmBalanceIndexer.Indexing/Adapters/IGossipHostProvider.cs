namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Supplies the base URLs of archival gossip nodes to fetch blocks from.</summary>
public interface IGossipHostProvider
{
    Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken);
}
