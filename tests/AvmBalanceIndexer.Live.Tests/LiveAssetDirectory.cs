using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Resolves the assets a mid-chain block destroys or reconfigures, since the live tests do not index from genesis: the creation
/// round comes from the public indexer and the record from normalizing the creating block.
/// </summary>
public static class LiveAssetDirectory
{
    public static async Task<AssetDirectory> LoadAsync(ReferenceNode reference, AlgorandChainAdapter adapter, BlockData block)
    {
        var normalizer = new AlgoEffectNormalizer();
        var records = new List<TokenInfo>();
        foreach (var assetId in AssetScanner.ReferencedAssets(block))
        {
            var (_, createdAt) = await reference.AssetAsync(assetId);
            var creation = await adapter.FetchBlockAsync(createdAt, CancellationToken.None);
            var previous = await adapter.FetchBlockAsync(createdAt - 1, CancellationToken.None);
            var created = normalizer.NormalizeBlock(creation, RewardsState.FromHeader(previous.Block), await LoadAsync(reference, adapter, creation))
                .TokenChanges.LastOrDefault(t => t.Key == TokenKey.Asa(assetId));
            records.Add(created ?? throw new InvalidOperationException($"Asset {assetId} was not created in round {createdAt}."));
        }

        return new AssetDirectory(records);
    }
}
