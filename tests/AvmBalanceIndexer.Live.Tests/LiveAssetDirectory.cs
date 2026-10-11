using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>Resolves the assets a mid-chain block destroys from the public indexer and the creating block, since the live tests do not index from genesis.</summary>
public static class LiveAssetDirectory
{
    public static async Task<AssetDirectory> LoadAsync(ReferenceNode reference, AlgorandChainAdapter adapter, BlockData block)
    {
        var records = new List<TokenInfo>();
        foreach (var assetId in AssetScanner.DestroyedAssets(block))
        {
            // The indexer reports a destroyed asset with total 0, so the supply is read from the creating transaction.
            var (creator, createdAt) = await reference.AssetAsync(assetId);
            var creation = await adapter.FetchBlockAsync(createdAt, CancellationToken.None);
            var parameters = FindCreation(creation.ApplyData, assetId) ?? throw new InvalidOperationException($"Asset {assetId} was not created in round {createdAt}.");
            records.Add(AssetRecords.Created(assetId, creator, parameters, createdAt));
        }

        return new AssetDirectory(records);
    }

    private static RawAssetParams? FindCreation(IEnumerable<TxnApplyData> transactions, ulong assetId)
    {
        foreach (var apply in transactions)
        {
            if (apply.CreatedAssetId == assetId)
            {
                return RawTransaction.Read(apply.RawTxn).Params;
            }

            if (FindCreation(apply.InnerTxns, assetId) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }
}
