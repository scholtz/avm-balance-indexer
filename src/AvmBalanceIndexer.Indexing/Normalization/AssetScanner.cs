using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>Finds what a block needs to know about assets before it can be normalized.</summary>
public static class AssetScanner
{
    /// <summary>Ids of the assets the block destroys, inner transactions included.</summary>
    public static IReadOnlySet<ulong> DestroyedAssets(BlockData block) => ReferencedAssets(block, destroyedOnly: true);

    /// <summary>Ids of the assets the block destroys or reconfigures, or only those it destroys.</summary>
    /// <param name="block">The block to scan.</param>
    /// <param name="destroyedOnly">Leave out the assets that are only reconfigured.</param>
    public static IReadOnlySet<ulong> ReferencedAssets(BlockData block, bool destroyedOnly = false)
    {
        ArgumentNullException.ThrowIfNull(block);
        var result = new HashSet<ulong>();
        foreach (var apply in block.ApplyData)
        {
            Collect(apply, result, destroyedOnly);
        }

        return result;
    }

    private static void Collect(TxnApplyData apply, HashSet<ulong> into, bool destroyedOnly)
    {
        if (!apply.RawTxn.IsEmpty && RawTransaction.Read(apply.RawTxn) is { Type: "acfg", ConfigAssetId: not 0 } configure && (configure.Params is null || !destroyedOnly))
        {
            into.Add(configure.ConfigAssetId);
        }

        foreach (var inner in apply.InnerTxns)
        {
            Collect(inner, into, destroyedOnly);
        }
    }
}
