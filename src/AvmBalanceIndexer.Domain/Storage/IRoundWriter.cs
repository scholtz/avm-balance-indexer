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

    /// <summary>
    /// Persists several consecutive rounds as one unit and publishes the last of them. Nothing of the batch is visible before
    /// the last round is published, and the cursor never points into the middle of a batch. A backfill commits many rounds per
    /// call because a commit costs several database round trips regardless of its size.
    /// </summary>
    /// <remarks>
    /// The first round must directly follow the published one (round 0 for an empty database). A batch of exactly one round
    /// behaves like <see cref="WriteRoundAsync"/>, including the idempotent replay of the latest published round.
    /// </remarks>
    /// <returns>True when the batch was applied; false when it was a replay of the latest published round.</returns>
    /// <exception cref="ChainDivergenceException">The first round does not follow the published cursor.</exception>
    /// <exception cref="ArgumentException">The batch is not valid, see <see cref="RoundWrite.EnsureValid"/>.</exception>
    Task<bool> WriteRoundsAsync(IReadOnlyList<RoundWrite> rounds, CancellationToken cancellationToken);
}
