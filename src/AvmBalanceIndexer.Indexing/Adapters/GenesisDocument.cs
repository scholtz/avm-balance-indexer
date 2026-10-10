using System.Text.Json;
using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>One genesis allocation: an account and its initial native balance in base units.</summary>
public sealed record GenesisAccount(Address Address, ulong Balance, string? Comment);

/// <summary>The parts of a genesis file the indexer needs.</summary>
/// <param name="SchemaId">The <c>id</c> field of the genesis file, for example <c>v1.0</c>.</param>
/// <param name="Network">The <c>network</c> field, for example <c>mainnet</c>.</param>
/// <param name="Accounts">Funded and unfunded genesis allocations.</param>
public sealed record GenesisDocument(string SchemaId, string Network, IReadOnlyList<GenesisAccount> Accounts)
{
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
                accounts);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Genesis document is malformed.", ex);
        }
    }
}
