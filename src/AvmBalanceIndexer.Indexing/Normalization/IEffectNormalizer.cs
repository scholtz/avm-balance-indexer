using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>Derives deterministic balance deltas from a block, including inner transactions.</summary>
public interface IEffectNormalizer
{
    /// <summary>Returns the deltas in ledger execution order with deterministic effect ids.</summary>
    /// <param name="block">The block to process.</param>
    /// <param name="previousRewards">Rewards state of the previous block, needed for the rewards pool withdrawal.</param>
    /// <exception cref="InvalidDataException">The block destroys an asset that is not in the stateless directory.</exception>
    IReadOnlyList<BalanceDelta> Normalize(BlockData block, RewardsState previousRewards);

    /// <summary>
    /// Like <see cref="Normalize"/>, with the token records the block creates, destroys or reconfigures. Destroying and
    /// reconfiguring need the stored record of the asset in <paramref name="assets"/>.
    /// </summary>
    /// <param name="block">The block to process.</param>
    /// <param name="previousRewards">Rewards state of the previous block.</param>
    /// <param name="assets">Records of the assets the block destroys or reconfigures (see <see cref="AssetScanner.ReferencedAssets"/>).</param>
    NormalizedBlock NormalizeBlock(BlockData block, RewardsState previousRewards, IAssetDirectory assets);
}
