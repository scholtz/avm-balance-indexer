using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>Derives deterministic balance deltas from a block, including inner transactions.</summary>
public interface IEffectNormalizer
{
    /// <summary>Returns the deltas in ledger execution order with deterministic effect ids.</summary>
    /// <param name="block">The block to process.</param>
    /// <param name="previousRewards">Rewards state of the previous block, needed for the rewards pool withdrawal.</param>
    IReadOnlyList<BalanceDelta> Normalize(BlockData block, RewardsState previousRewards);
}
