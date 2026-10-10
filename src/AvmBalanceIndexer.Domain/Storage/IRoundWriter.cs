using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Write side of the storage contract. Writes must be idempotent on <see cref="EffectId"/> so that a round can be
/// replayed safely. PostgreSQL implements this as one transaction; Cassandra as idempotent upserts followed by
/// publication through <see cref="IChainStatusStore.PublishRoundAsync"/>.
/// </summary>
public interface IRoundWriter
{
    Task WriteRoundAsync(ChainStatus roundStatus, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken);
}
