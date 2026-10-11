using System.Globalization;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using DomainAddress = AvmBalanceIndexer.Domain.Chains.Address;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>
/// Builds and reads the stored token record of an ASA. The metadata json keeps what the record has no column for: the total
/// supply, the url, whether holdings start frozen and the four management addresses.
/// </summary>
public static class AssetRecords
{
    /// <summary>
    /// The record of an asset as created by its creation transaction.
    /// </summary>
    /// <param name="assetId">Id the ledger assigned to the asset.</param>
    /// <param name="creator">Creating account.</param>
    /// <param name="parameters">Creation parameters.</param>
    /// <param name="round">Round of the creation.</param>
    /// <param name="issuingApplication">Application that issued the creation as an inner transaction, zero for a top-level creation.</param>
    public static TokenInfo Created(ulong assetId, DomainAddress creator, RawAssetParams parameters, ulong round, ulong issuingApplication = 0)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var info = new TokenInfo(
            TokenKey.Asa(assetId),
            creator,
            (int)parameters.Decimals,
            Clean(parameters.UnitName),
            Clean(parameters.Name),
            TokenStandard.Asa,
            StandardVerified: true,
            ControllingAppId: null,
            CreatedRound: round,
            DeletedRound: null,
            Metadata(parameters.Total, Clean(parameters.Url), parameters, issuingApplication));
        return Classify(info, parameters.DefaultFrozen, parameters.Clawback, issuingApplication);
    }

    /// <summary>The record after an asset configuration changed the management addresses.</summary>
    public static TokenInfo Reconfigured(TokenInfo info, RawAssetParams parameters)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(parameters);

        // Only the four addresses can change; an address left out of the transaction is cleared.
        var current = ReadMetadata(info);
        if (!current.HasIssuingApplication)
        {
            // Written before the issuing application was kept: the classification cannot be re-evaluated, so it stays.
            return info with { MetadataJson = Metadata(Total(info), current.Url, parameters with { DefaultFrozen = current.DefaultFrozen }, 0) };
        }

        var reconfigured = info with
        {
            MetadataJson = Metadata(Total(info), current.Url, parameters with { DefaultFrozen = current.DefaultFrozen }, current.IssuingApplication),
        };
        return Classify(reconfigured, current.DefaultFrozen, parameters.Clawback, current.IssuingApplication);
    }

    // Asset names are arbitrary bytes and often padded with NUL, which PostgreSQL text and jsonb columns reject.
    private static string? Clean(string? text) => text?.Replace("\0", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// ARC-20 Smart ASAs are created by their controlling application, hold the application account as clawback and start
    /// frozen. All three are visible on chain, but another contract can create such an asset too, so the standard is reported
    /// as unverified (like ARC-200); the ASA balance stays the only balance stream.
    /// </summary>
    private static TokenInfo Classify(TokenInfo info, bool defaultFrozen, byte[]? clawback, ulong application)
    {
        var smart = application != 0
            && defaultFrozen
            && clawback is not null
            && clawback.AsSpan().SequenceEqual(ApplicationAddresses.Of(application).Bytes);
        return smart
            ? info with { Standard = TokenStandard.Arc20, StandardVerified = false, ControllingAppId = application }
            : info with { Standard = TokenStandard.Asa, StandardVerified = true, ControllingAppId = null };
    }

    private static string Metadata(ulong total, string? url, RawAssetParams parameters, ulong issuingApplication) =>
        JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["total"] = total.ToString(CultureInfo.InvariantCulture),
            ["url"] = url,
            ["defaultFrozen"] = parameters.DefaultFrozen ? "true" : "false",
            ["issuingApplication"] = issuingApplication == 0 ? null : issuingApplication.ToString(CultureInfo.InvariantCulture),
            ["manager"] = Text(parameters.Manager),
            ["reserve"] = Text(parameters.Reserve),
            ["freeze"] = Text(parameters.Freeze),
            ["clawback"] = Text(parameters.Clawback),
        });

    private static string? Text(byte[]? address) =>
        address is null || address.All(static b => b == 0) ? null : new Algorand.Address(address).EncodeAsString();

    // Records written before the addresses were kept have none of these fields; they read as unset.
    private static (string? Url, bool DefaultFrozen, ulong IssuingApplication, bool HasIssuingApplication) ReadMetadata(TokenInfo info)
    {
        using var document = JsonDocument.Parse(info.MetadataJson ?? "{}");
        var root = document.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return (
            Text("url"),
            Text("defaultFrozen") == "true",
            ulong.TryParse(Text("issuingApplication"), NumberStyles.None, CultureInfo.InvariantCulture, out var application) ? application : 0,
            root.TryGetProperty("issuingApplication", out _));
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
