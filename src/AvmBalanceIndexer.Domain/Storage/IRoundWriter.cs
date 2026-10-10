using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Write side of the storage contract. Writes are idempotent on the effect identity so that a round can be
/// replayed safely. PostgreSQL implements this as one transaction; Cassandra as idempotent upserts followed by
/// publication through the chain status store.
/// </summary>
public interface IRoundWriter
{
    /// <summary>
    /// Persists all effects of the round carried by <paramref name="roundStatus"/>, updates current
    /// holdings and touched accounts, and publishes the round as the final step.
    /// Writing the latest published round again with the same block hash changes nothing and returns false.
    /// </summary>
    /// <returns>True when the round was applied; false when it was already published.</returns>
    /// <exception cref="ChainDivergenceException">
    /// The round skips ahead of the published cursor, is older than it (its hash can no longer be verified), or has a
    /// block hash that differs from the published one.
    /// </exception>
    Task<bool> WriteRoundAsync(ChainStatus roundStatus, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken);
}
