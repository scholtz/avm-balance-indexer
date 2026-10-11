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
/// <param name="DefaultFrozen">Holdings start frozen (<c>df</c>).</param>
/// <param name="Manager">Manager address (<c>m</c>), null when absent.</param>
/// <param name="Reserve">Reserve address (<c>r</c>), null when absent.</param>
/// <param name="Freeze">Freeze address (<c>f</c>), null when absent.</param>
/// <param name="Clawback">Clawback address (<c>c</c>), null when absent.</param>
public sealed record RawAssetParams(
    ulong Total,
    uint Decimals,
    string? UnitName,
    string? Name,
    string? Url,
    bool DefaultFrozen = false,
    byte[]? Manager = null,
    byte[]? Reserve = null,
    byte[]? Freeze = null,
    byte[]? Clawback = null);

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
/// <param name="ApplicationId">Application called (<c>apid</c>), zero for an application creation.</param>
/// <param name="Sender">Sending account (<c>snd</c>).</param>
/// <param name="Fee">Fee paid by the sender (<c>fee</c>), zero when omitted.</param>
/// <param name="Receiver">Payment receiver (<c>rcv</c>); the field is omitted for the zero address, which reads as null.</param>
/// <param name="Amount">Payment amount (<c>amt</c>), zero when omitted.</param>
/// <param name="CloseTo">Payment close-remainder-to (<c>close</c>), null when absent.</param>
public sealed record RawTransaction(
    string Type,
    ulong TransferAssetId,
    ulong TransferAmount,
    byte[]? AssetSender,
    byte[]? AssetReceiver,
    byte[]? AssetCloseTo,
    ulong ConfigAssetId,
    RawAssetParams? Params,
    ulong ApplicationId = 0,
    byte[]? Sender = null,
    ulong Fee = 0,
    byte[]? Receiver = null,
    ulong Amount = 0,
    byte[]? CloseTo = null)
{
    private const int AddressLength = 32;

    /// <summary>Reads the fields from the raw <c>txn</c> map.</summary>
    /// <exception cref="InvalidDataException">The map is malformed.</exception>
    public static RawTransaction Read(ReadOnlyMemory<byte> raw)
    {
        var reader = new MessagePackReader(raw);
        var type = string.Empty;
        ulong xaid = 0, aamt = 0, caid = 0, apid = 0, fee = 0, amt = 0;
        byte[]? snd = null, rcv = null, close = null;
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
                case "apid":
                    apid = reader.ReadUInt64();
                    break;
                case "snd":
                    snd = ReadAddress(ref reader);
                    break;
                case "fee":
                    fee = reader.ReadUInt64();
                    break;
                case "rcv":
                    rcv = ReadAddress(ref reader);
                    break;
                case "amt":
                    amt = reader.ReadUInt64();
                    break;
                case "close":
                    close = ReadAddress(ref reader);
                    break;
                case "apar":
                    parameters = ReadParams(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new RawTransaction(type, xaid, aamt, asnd, arcv, aclose, caid, parameters, apid, snd, fee, rcv, amt, close);
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
        var defaultFrozen = false;
        byte[]? manager = null, reserve = null, freeze = null, clawback = null;
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
                case "df":
                    defaultFrozen = reader.ReadBoolean();
                    break;
                case "m":
                    manager = ReadAddress(ref reader);
                    break;
                case "r":
                    reserve = ReadAddress(ref reader);
                    break;
                case "f":
                    freeze = ReadAddress(ref reader);
                    break;
                case "c":
                    clawback = ReadAddress(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new RawAssetParams(total, decimals, unit, name, url, defaultFrozen, manager, reserve, freeze, clawback);
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
