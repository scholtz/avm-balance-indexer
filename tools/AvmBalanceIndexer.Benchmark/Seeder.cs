using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;

namespace AvmBalanceIndexer.Benchmark;

/// <summary>
/// Makes a mid-chain range indexable from an empty database: a dry run over the range finds, per account and token, how
/// much it must already have held for no balance to go negative, and those amounts are written as the genesis round. The
/// production indexer then processes the range unchanged. The dry run also yields the activity statistics the API load uses.
/// </summary>
public sealed class Seeder
{
    private readonly Dictionary<HoldingKey, BigInteger> _running = [];
    private readonly Dictionary<HoldingKey, BigInteger> _lowest = [];
    private readonly Dictionary<HoldingKey, int> _touches = [];

    public IReadOnlyDictionary<HoldingKey, int> Touches => _touches;

    public int SeededAccounts { get; private set; }

    /// <summary>Normalizes the whole set once, exactly as the indexer will.</summary>
    public void DryRun(BlockSet blocks, IEffectNormalizer normalizer)
    {
        var rewards = default(RewardsState);
        var assets = new Dictionary<ulong, TokenInfo>();
        foreach (var block in blocks.Blocks)
        {
            // The indexer finds the records of assets created earlier in the range in its token store; the dry run keeps them here.
            var normalized = normalizer.NormalizeBlock(block, rewards, new AssetDirectory(assets.Values));
            foreach (var change in normalized.TokenChanges.Where(static t => t.Key.Kind == TokenKind.Asa))
            {
                assets[change.Key.TokenId] = change;
            }

            foreach (var delta in normalized.Deltas)
            {
                var key = new HoldingKey(delta.Account, delta.Token);
                var now = _running.GetValueOrDefault(key) + delta.Delta;
                _running[key] = now;
                if (now < _lowest.GetValueOrDefault(key))
                {
                    _lowest[key] = now;
                }

                _touches[key] = _touches.GetValueOrDefault(key) + 1;
            }

            rewards = RewardsState.FromHeader(block.Block);
        }
    }

    /// <summary>Writes round 0 with the balances the range needs and the native token record.</summary>
    public async Task WriteGenesisAsync(IChainStorage storage, BlockSet blocks, CancellationToken cancellationToken)
    {
        var seeds = _lowest.Where(static pair => pair.Value.Sign < 0).OrderBy(static pair => pair.Key.Token.Kind).ThenBy(static pair => pair.Key.Token.TokenId).ToArray();
        var effects = seeds
            .Select((pair, ordinal) => new TokenEffect(
                new EffectId(0, 0, string.Empty, [], ordinal),
                pair.Key.Account,
                pair.Key.Token,
                EffectType.Genesis,
                -pair.Value,
                new Amount(-pair.Value),
                null))
            .ToArray();
        SeededAccounts = effects.Length;

        await storage.UpsertAsync(
            new TokenInfo(TokenKey.Native, null, 6, blocks.Profile.NativeSymbol, blocks.Profile.NativeName, TokenStandard.Native, true, null, 0, null, null),
            cancellationToken).ConfigureAwait(false);
        var status = new ChainStatus(blocks.Profile.Chain, 0, blocks.BeforeFirstHash, (ulong)blocks.Rounds, IngestionState.Bootstrapping, DateTimeOffset.UtcNow);
        await storage.WriteRoundAsync(status, effects, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Seeded round 0 with {effects.Length} starting balances");
    }
}

/// <summary>
/// The production normalizer, except that the first round starts from the rewards state of the real block before the range and that destroying an asset created before the range does not fail: the replay never saw its
/// creation, so a record with nothing in it stands in (the destroy then moves no units).
/// </summary>
public sealed class LenientNormalizer(AlgoEffectNormalizer inner, RewardsState beforeFirstRound) : IEffectNormalizer
{
    public IReadOnlyList<BalanceDelta> Normalize(BlockData block, RewardsState previousRewards) =>
        NormalizeBlock(block, previousRewards, AssetDirectory.Empty).Deltas;

    public NormalizedBlock NormalizeBlock(BlockData block, RewardsState previousRewards, IAssetDirectory assets) =>
        // The indexer starts a fresh chain with the genesis rewards state; the real one is that of the block before the range.
        inner.NormalizeBlock(block, block.Round == 1 ? beforeFirstRound : previousRewards, new Synthetic(assets));

    private sealed class Synthetic(IAssetDirectory real) : IAssetDirectory
    {
        public TokenInfo? Find(ulong assetId) =>
            real.Find(assetId) ?? AssetRecords.Created(assetId, new Address(new byte[Address.Length]), new RawAssetParams(0, 0, null, null, null), 0);
    }
}
