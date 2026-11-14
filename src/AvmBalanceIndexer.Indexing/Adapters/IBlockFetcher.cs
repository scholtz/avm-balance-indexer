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

    /// <summary>
    /// Tells the fetcher that <paramref name="block"/> validated but does not belong to the chain: the archival nodes serve another block
    /// of its round that links to the published one. A fetcher that took it from a relay rests and reports that relay; the default ignores it.
    /// </summary>
    void ReportWrongBlock(BlockData block)
    {
    }
}
