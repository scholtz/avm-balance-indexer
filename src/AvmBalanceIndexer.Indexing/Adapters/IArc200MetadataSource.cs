using AvmBalanceIndexer.Indexing.Normalization;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Reads the metadata of an ARC-200 application from the chain (enrichment, never ledger state).</summary>
public interface IArc200MetadataSource
{
    /// <summary>Asks the application for its name, symbol, decimals and total supply as of now.</summary>
    /// <param name="applicationId">The ARC-200 application.</param>
    /// <param name="sender">The account the read-only calls are simulated from (the fee sink: it always exists and is funded).</param>
    /// <param name="latestRound">The chain's latest round, for the validity window of the simulated calls.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// Null when the node could not be asked (outage, throttling, timeout, malformed answer); the caller retries later. A record
    /// with null members means the application answered but does not implement (or malforms) those methods.
    /// </returns>
    Task<Arc200Metadata?> TryReadAsync(ulong applicationId, ReadOnlyMemory<byte> sender, ulong latestRound, CancellationToken cancellationToken);
}
