namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Chain-specific block source (archival algod / Conduit). One adapter instance per chain deployment.
/// Voi compatibility must be validated separately from Algorand MainNet/TestNet.
/// </summary>
public interface IChainAdapter
{
    string Chain { get; }

    Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken);

    Task<RawBlock> GetBlockAsync(ulong round, CancellationToken cancellationToken);
}
