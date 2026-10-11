using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>Finds what a block needs to know about assets before it can be normalized.</summary>
public static class AssetScanner
{
    /// <summary>Ids of the assets destroyed by transactions of the block, inner transactions included.</summary>
    public static IReadOnlySet<ulong> DestroyedAssets(BlockData block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var result = new HashSet<ulong>();
        foreach (var apply in block.ApplyData)
        {
            Collect(apply, result);
        }

        return result;
    }

    private static void Collect(TxnApplyData apply, HashSet<ulong> into)
    {
        if (!apply.RawTxn.IsEmpty && RawTransaction.Read(apply.RawTxn) is { Type: "acfg", ConfigAssetId: not 0, Params: null } destroy)
        {
            into.Add(destroy.ConfigAssetId);
        }

        foreach (var inner in apply.InnerTxns)
        {
            Collect(inner, into);
        }
    }
}
