using System.Buffers;
using System.Text;
using MessagePack;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Asset parameters of an asset creation (<c>apar</c>).</summary>
/// <param name="Total">Total supply in base units (<c>t</c>).</param>
/// <param name="Decimals">Decimal places (<c>dc</c>).</param>
/// <param name="UnitName">Unit name (<c>un</c>).</param>
/// <param name="Name">Asset name (<c>an</c>).</param>
/// <param name="Url">Asset url (<c>au</c>).</param>
public sealed record RawAssetParams(ulong Total, uint Decimals, string? UnitName, string? Name, string? Url);

/// <summary>
/// The asset related fields of a stored transaction, read from its canonical msgpack map. The Algorand5 model picks a
/// transaction subclass from the fields present; reading the map directly treats top-level and inner transactions the same
/// way and cannot be thrown off by a combination the SDK does not model.
/// </summary>
/// <param name="Type">Transaction type (<c>type</c>), for example <c>axfer</c> or <c>acfg</c>.</param>
/// <param name="TransferAssetId">Asset moved by an asset transfer (<c>xaid</c>).</param>
/// <param name="TransferAmount">Units moved (<c>aamt</c>).</param>
/// <param name="AssetSender">Clawback source (<c>asnd</c>), null when the transaction sender sends.</param>
/// <param name="AssetReceiver">Receiver (<c>arcv</c>); null is the zero address.</param>
/// <param name="AssetCloseTo">Close-out destination (<c>aclose</c>), null when absent.</param>
/// <param name="ConfigAssetId">Asset reconfigured or destroyed by an asset configuration (<c>caid</c>), zero when it creates one.</param>
/// <param name="Params">Asset parameters (<c>apar</c>), null for a destroy.</param>
public sealed record RawTransaction(
    string Type,
    ulong TransferAssetId,
    ulong TransferAmount,
    byte[]? AssetSender,
    byte[]? AssetReceiver,
    byte[]? AssetCloseTo,
    ulong ConfigAssetId,
    RawAssetParams? Params)
{
    private const int AddressLength = 32;

    /// <summary>Reads the fields from the raw <c>txn</c> map.</summary>
    /// <exception cref="InvalidDataException">The map is malformed.</exception>
    public static RawTransaction Read(ReadOnlyMemory<byte> raw)
    {
        var reader = new MessagePackReader(raw);
        var type = string.Empty;
        ulong xaid = 0, aamt = 0, caid = 0;
        byte[]? asnd = null, arcv = null, aclose = null;
        RawAssetParams? parameters = null;

        var entries = reader.ReadMapHeader();
        for (var i = 0; i < entries; i++)
        {
            switch (reader.ReadString())
            {
                case "type":
                    type = reader.ReadString() ?? string.Empty;
                    break;
                case "xaid":
                    xaid = reader.ReadUInt64();
                    break;
                case "aamt":
                    aamt = reader.ReadUInt64();
                    break;
                case "asnd":
                    asnd = ReadAddress(ref reader);
                    break;
                case "arcv":
                    arcv = ReadAddress(ref reader);
                    break;
                case "aclose":
                    aclose = ReadAddress(ref reader);
                    break;
                case "caid":
                    caid = reader.ReadUInt64();
                    break;
                case "apar":
                    parameters = ReadParams(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new RawTransaction(type, xaid, aamt, asnd, arcv, aclose, caid, parameters);
    }

    private static byte[] ReadAddress(ref MessagePackReader reader)
    {
        var bytes = reader.ReadBytes()?.ToArray();
        return bytes is { Length: AddressLength } ? bytes : throw new InvalidDataException("An address field is not 32 bytes.");
    }

    private static RawAssetParams? ReadParams(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        ulong total = 0;
        uint decimals = 0;
        string? unit = null, name = null, url = null;
        var entries = reader.ReadMapHeader();
        for (var i = 0; i < entries; i++)
        {
            switch (reader.ReadString())
            {
                case "t":
                    total = reader.ReadUInt64();
                    break;
                case "dc":
                    decimals = reader.ReadUInt32();
                    break;
                case "un":
                    unit = ReadText(ref reader);
                    break;
                case "an":
                    name = ReadText(ref reader);
                    break;
                case "au":
                    url = ReadText(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new RawAssetParams(total, decimals, unit, name, url);
    }

    private static string? ReadText(ref MessagePackReader reader)
    {
        if (reader.NextMessagePackType == MessagePackType.Binary)
        {
            return Encoding.UTF8.GetString(reader.ReadBytes()?.ToArray() ?? []);
        }

        return reader.ReadString();
    }
}
