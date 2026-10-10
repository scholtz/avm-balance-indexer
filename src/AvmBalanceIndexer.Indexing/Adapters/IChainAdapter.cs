namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Chain-specific data source: latest round from an algod node, genesis allocations, and archival blocks.
/// One adapter instance per chain deployment. Voi compatibility must be validated separately from Algorand networks.
/// </summary>
public interface IChainAdapter : IBlockFetcher
{
    ChainProfile Profile { get; }

    Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken);

    Task<GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken);
}
