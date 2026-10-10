using System.Buffers;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Chains;
using MessagePack;
using Org.BouncyCastle.Crypto.Digests;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>One genesis allocation: an account and its initial native balance in base units.</summary>
public sealed record GenesisAccount(Address Address, ulong Balance, string? Comment);

/// <summary>The parts of a genesis file the indexer needs.</summary>
/// <param name="SchemaId">The <c>id</c> field of the genesis file, for example <c>v1.0</c>.</param>
/// <param name="Network">The <c>network</c> field, for example <c>mainnet</c>.</param>
/// <param name="Accounts">Funded and unfunded genesis allocations.</param>
/// <param name="Hash">
/// The genesis hash computed from the file the way the ledger does, so it can be compared with the pinned genesis hash.
/// </param>
public sealed record GenesisDocument(string SchemaId, string Network, IReadOnlyList<GenesisAccount> Accounts, byte[] Hash)
{
    /// <summary>Genesis fields whose JSON value is base64 but whose msgpack value is binary.</summary>
    private static readonly HashSet<string> BinaryFields = new(StringComparer.Ordinal) { "sel", "vote", "stprf" };

    /// <summary>The id blocks carry in their header: the network and the schema id, for example <c>mainnet-v1.0</c>.</summary>
    public string Id => string.IsNullOrEmpty(Network) ? SchemaId : $"{Network}-{SchemaId}";

    /// <summary>Parses the JSON served by <c>GET /genesis</c> on an algod node.</summary>
    /// <exception cref="InvalidDataException">The document is malformed or contains duplicate accounts.</exception>
    public static GenesisDocument Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var accounts = new List<GenesisAccount>();
            var seen = new HashSet<Address>();
            foreach (var alloc in root.GetProperty("alloc").EnumerateArray())
            {
                var text = alloc.GetProperty("addr").GetString() ?? throw new InvalidDataException("Genesis allocation without address.");
                var address = new Address(new Algorand.Address(text).Bytes);
                if (!seen.Add(address))
                {
                    throw new InvalidDataException($"Genesis lists account {text} twice.");
                }

                var balance = alloc.GetProperty("state").GetProperty("algo").GetUInt64();
                var comment = alloc.TryGetProperty("comment", out var c) ? c.GetString() : null;
                accounts.Add(new GenesisAccount(address, balance, comment));
            }

            return new GenesisDocument(
                root.GetProperty("id").GetString() ?? throw new InvalidDataException("Genesis has no id."),
                root.TryGetProperty("network", out var network) ? network.GetString() ?? string.Empty : string.Empty,
                accounts,
                ComputeHash(root));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Genesis document is malformed.", ex);
        }
    }

    /// <summary>
    /// SHA-512/256 over <c>"GE"</c> and the canonical msgpack of the genesis document: object keys sorted, minimal integer
    /// encodings, base64 key material as binary. Reproduces the pinned hashes of Algorand MainNet, TestNet and Voi.
    /// </summary>
    private static byte[] ComputeHash(JsonElement root)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        WriteCanonical(ref writer, root, null);
        writer.Flush();

        var digest = new Sha512tDigest(256);
        digest.BlockUpdate("GE"u8);
        digest.BlockUpdate(buffer.WrittenSpan);
        var hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        return hash;
    }

    private static void WriteCanonical(ref MessagePackWriter writer, JsonElement element, string? property)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var members = element.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
                writer.WriteMapHeader(members.Length);
                foreach (var member in members)
                {
                    writer.Write(member.Name);
                    WriteCanonical(ref writer, member.Value, member.Name);
                }

                break;
            case JsonValueKind.Array:
                writer.WriteArrayHeader(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(ref writer, item, null);
                }

                break;
            case JsonValueKind.String when property is not null && BinaryFields.Contains(property):
                writer.Write(Convert.FromBase64String(element.GetString()!));
                break;
            case JsonValueKind.String:
                writer.Write(element.GetString());
                break;
            case JsonValueKind.Number when element.TryGetUInt64(out var unsigned):
                writer.Write(unsigned);
                break;
            case JsonValueKind.Number when element.TryGetInt64(out var signed):
                writer.Write(signed);
                break;
            case JsonValueKind.True:
                writer.Write(true);
                break;
            case JsonValueKind.False:
                writer.Write(false);
                break;
            default:
                throw new InvalidDataException($"Genesis contains an unsupported {element.ValueKind} value.");
        }
    }
}
