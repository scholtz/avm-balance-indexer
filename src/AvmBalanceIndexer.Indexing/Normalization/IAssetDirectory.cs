using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>
/// Known assets, needed to destroy one: the ledger destroys the creator's whole holding, which a block does not state.
/// Assets created in the same block are tracked by the normalizer itself.
/// </summary>
public interface IAssetDirectory
{
    /// <summary>Returns the stored record of an asset, or null when the indexer has never seen it created.</summary>
    TokenInfo? Find(ulong assetId);
}

/// <summary>An <see cref="IAssetDirectory"/> over a fixed set of records.</summary>
public sealed class AssetDirectory(IEnumerable<TokenInfo> assets) : IAssetDirectory
{
    private readonly Dictionary<ulong, TokenInfo> _assets = assets.ToDictionary(static a => a.Key.TokenId);

    /// <summary>A directory that knows no asset.</summary>
    public static AssetDirectory Empty { get; } = new([]);

    public TokenInfo? Find(ulong assetId) => _assets.GetValueOrDefault(assetId);
}
