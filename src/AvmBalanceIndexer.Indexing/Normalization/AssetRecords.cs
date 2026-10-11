using System.Globalization;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using DomainAddress = AvmBalanceIndexer.Domain.Chains.Address;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>Builds and reads the stored token record of an ASA. The total supply is kept in the metadata json.</summary>
public static class AssetRecords
{
    /// <summary>The record of an asset as created by its creation transaction.</summary>
    public static TokenInfo Created(ulong assetId, DomainAddress creator, RawAssetParams parameters, ulong round)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var metadata = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["total"] = parameters.Total.ToString(CultureInfo.InvariantCulture),
            ["url"] = parameters.Url,
        });

        return new TokenInfo(
            TokenKey.Asa(assetId),
            creator,
            (int)parameters.Decimals,
            parameters.UnitName,
            parameters.Name,
            TokenStandard.Asa,
            StandardVerified: true,
            ControllingAppId: null,
            CreatedRound: round,
            DeletedRound: null,
            metadata);
    }

    /// <summary>Total supply recorded at creation.</summary>
    /// <exception cref="InvalidDataException">The record carries no total.</exception>
    public static ulong Total(TokenInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.MetadataJson is { } json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("total", out var total) && ulong.TryParse(total.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        throw new InvalidDataException($"Asset {info.Key.TokenId} has no recorded total supply.");
    }
}
