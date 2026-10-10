using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Publication cursor and ingestion journal. Advancing the cursor is the final step of a round commit.</summary>
public interface IChainStatusStore
{
    Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken);

    Task PublishRoundAsync(ChainStatus status, CancellationToken cancellationToken);
}
