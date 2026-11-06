namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Fetches validated blocks of one chain.</summary>
public interface IBlockFetcher
{
    /// <exception cref="BlockNotAvailableException">No node had the block.</exception>
    /// <exception cref="BlockFetchException">The block could not be fetched or validated.</exception>
    Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a block while following the chain tip. <paramref name="latestRound"/> is the newest round of the chain, which lets a
    /// fetcher ask nodes that only hold recent blocks (relays) for rounds close to it. The default ignores the hint.
    /// </summary>
    /// <exception cref="BlockNotAvailableException">No node had the block.</exception>
    /// <exception cref="BlockFetchException">The block could not be fetched or validated.</exception>
    Task<BlockData> FetchBlockAsync(ulong round, ulong latestRound, CancellationToken cancellationToken) =>
        FetchBlockAsync(round, cancellationToken);
}
