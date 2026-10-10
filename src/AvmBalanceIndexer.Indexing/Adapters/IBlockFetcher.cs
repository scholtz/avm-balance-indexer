namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Fetches validated blocks of one chain.</summary>
public interface IBlockFetcher
{
    /// <exception cref="BlockNotAvailableException">No node had the block.</exception>
    /// <exception cref="BlockFetchException">The block could not be fetched or validated.</exception>
    Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken);
}
