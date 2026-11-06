namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Supplies the base URLs of the gossip nodes to fetch blocks from.</summary>
public interface IGossipHostProvider
{
    /// <summary>Archival nodes: they serve every block of the chain.</summary>
    Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Relays and other nodes that serve only the newest blocks. Used in addition to the archival nodes for rounds near the tip.
    /// A failed lookup means no relays rather than an error: the archival nodes then carry the load.
    /// </summary>
    Task<IReadOnlyList<string>> GetRelayHostsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
