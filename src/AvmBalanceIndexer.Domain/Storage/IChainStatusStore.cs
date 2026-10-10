using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Publication cursor. Advancing the cursor is the final step of a round commit.</summary>
public interface IChainStatusStore
{
    Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken);

    /// <summary>Updates target round, state and timestamp without moving the indexed round.</summary>
    Task UpdateProgressAsync(ChainStatus status, CancellationToken cancellationToken);
}
