using System.Net;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Independent source of truth for the live tests: a public algod (ledger state deltas) and a public indexer
/// (transaction history). Neither shares code with the indexer under test.
/// </summary>
public sealed class ReferenceNode : IDisposable
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _algod;
    private readonly string _indexer;
    private readonly string? _arc200;

    public ReferenceNode(string algodUrl, string indexerUrl, string? arc200Url = null)
    {
        _algod = algodUrl.TrimEnd('/');
        _indexer = indexerUrl.TrimEnd('/');
        _arc200 = arc200Url?.TrimEnd('/');
    }

    public static ReferenceNode TestNet() => new("https://testnet-api.4160.nodely.dev", "https://testnet-idx.4160.nodely.dev");

    public static ReferenceNode Voi() => new("https://mainnet-api.voi.nodely.dev", "https://mainnet-idx.voi.nodely.dev", "https://voi-mainnet-mimirapi.nftnavigator.xyz");

    public static ReferenceNode MainNet() => new("https://mainnet-api.4160.nodely.dev", "https://mainnet-idx.4160.nodely.dev");

    public static string Encode(Address address) => new Algorand.Address(address.ToArray()).EncodeAsString();

    /// <summary>All transactions confirmed in rounds 1 through <paramref name="maxRound"/> as seen by the indexer.</summary>
    public async Task<IReadOnlyList<ReferenceTransaction>> TransactionsUpToAsync(ulong maxRound)
    {
        var result = new List<ReferenceTransaction>();
        string? next = null;
        do
        {
            var url = $"{_indexer}/v2/transactions?max-round={maxRound}&limit=1000" + (next is null ? string.Empty : $"&next={Uri.EscapeDataString(next)}");
            using var json = await GetJsonAsync(url).ConfigureAwait(false);
            var root = json.RootElement;
            result.AddRange(root.GetProperty("transactions").EnumerateArray().Select(static t => ReferenceTransaction.From(t)));
            next = root.TryGetProperty("next-token", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : null;
        }
        while (next is not null);

        return result;
    }

    /// <summary>
    /// Transactions confirmed in rounds <paramref name="minRound"/> to <paramref name="maxRound"/> including inner
    /// transactions (flattened, parents first), as seen by the indexer.
    /// </summary>
    public async Task<IReadOnlyList<ReferenceTransaction>> TransactionsInRangeAsync(ulong minRound, ulong maxRound)
    {
        var result = new List<ReferenceTransaction>();
        string? next = null;
        do
        {
            var url = $"{_indexer}/v2/transactions?min-round={minRound}&max-round={maxRound}&limit=1000" + (next is null ? string.Empty : $"&next={Uri.EscapeDataString(next)}");
            using var json = await GetJsonAsync(url).ConfigureAwait(false);
            var root = json.RootElement;
            foreach (var transaction in root.GetProperty("transactions").EnumerateArray())
            {
                Flatten(transaction, result);
            }

            next = root.TryGetProperty("next-token", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : null;
        }
        while (next is not null);

        return result;
    }

    private static void Flatten(JsonElement transaction, List<ReferenceTransaction> into, bool inner = false)
    {
        into.Add(ReferenceTransaction.From(transaction, inner));
        if (transaction.TryGetProperty("inner-txns", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                Flatten(child, into, inner: true);
            }
        }
    }

    /// <summary>
    /// The exact balances the ledger itself holds after <paramref name="round"/> for every account that round changed,
    /// from algod ledger state deltas. This is the strongest available oracle: it is the node own post-round state.
    /// </summary>
    public async Task<IReadOnlyDictionary<Address, ulong>> LedgerBalancesAfterAsync(ulong round)
    {
        // Later sections of recent deltas carry integer map keys, which is not valid JSON, so only the leading Accts value is parsed.
        var text = await GetTextAsync($"{_algod}/v2/deltas/{round}").ConfigureAwait(false);
        using var json = JsonDocument.Parse(LeadingAccts(text));
        var result = new Dictionary<Address, ulong>();
        foreach (var account in json.RootElement.GetProperty("Accts").GetProperty("Accts").EnumerateArray())
        {
            var address = new Address(Convert.FromBase64String(account.GetProperty("Addr").GetString()!));
            result[address] = account.GetProperty("MicroAlgos").GetUInt64();
        }

        return result;
    }

    /// <summary>
    /// The asset holdings the ledger itself holds after <paramref name="round"/> for every (account, asset) that round changed.
    /// A removed holding (opt-out, destroy) is reported as zero.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Address Account, ulong AssetId), ulong>> LedgerAssetHoldingsAfterAsync(ulong round)
    {
        var text = await GetTextAsync($"{_algod}/v2/deltas/{round}").ConfigureAwait(false);
        using var json = JsonDocument.Parse(LeadingAccts(text));
        var result = new Dictionary<(Address, ulong), ulong>();
        if (!json.RootElement.GetProperty("Accts").TryGetProperty("AssetResources", out var resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var resource in resources.EnumerateArray())
        {
            var holding = resource.GetProperty("Holding");
            var deleted = holding.GetProperty("Deleted").GetBoolean();
            var data = holding.TryGetProperty("Holding", out var inner) ? inner : default;
            if (!deleted && data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var address = new Address(Convert.FromBase64String(resource.GetProperty("Addr").GetString()!));
            var amount = !deleted && data.TryGetProperty("a", out var a) ? a.GetUInt64() : 0UL;
            result[(address, resource.GetProperty("Aidx").GetUInt64())] = amount;
        }

        return result;
    }

    /// <summary>Creator and creation round of an asset as the public indexer reports them, including destroyed assets (whose total it reports as zero).</summary>
    public async Task<(Address Creator, ulong CreatedAtRound)> AssetAsync(ulong assetId)
    {
        using var json = await GetJsonAsync($"{_indexer}/v2/assets/{assetId}?include-all=true").ConfigureAwait(false);
        var asset = json.RootElement.GetProperty("asset");
        return (new Address(new Algorand.Address(asset.GetProperty("params").GetProperty("creator").GetString()!).Bytes), asset.GetProperty("created-at-round").GetUInt64());
    }

    /// <summary>Distinct rounds of the newest ARC-200 transfers the ARC indexer lists, newest first.</summary>
    public async Task<IReadOnlyList<ulong>> RecentArc200RoundsAsync(int transfers)
    {
        var url = _arc200 ?? throw new InvalidOperationException("This reference has no ARC-200 indexer.");
        using var json = await GetJsonAsync($"{url}/arc200/transfers?limit={transfers}").ConfigureAwait(false);
        return [.. json.RootElement.GetProperty("transfers").EnumerateArray().Select(static t => t.GetProperty("round").GetUInt64()).Distinct().OrderDescending()];
    }

    /// <summary>The ARC-200 transfers one round contains as reported by the Voi ARC indexer (Mimir).</summary>
    public async Task<IReadOnlyList<ReferenceArc200Transfer>> Arc200TransfersInRoundAsync(ulong round)
    {
        var url = _arc200 ?? throw new InvalidOperationException("This reference has no ARC-200 indexer.");
        using var json = await GetJsonAsync($"{url}/arc200/transfers?round={round}&limit=1000").ConfigureAwait(false);
        return
        [
            .. json.RootElement.GetProperty("transfers").EnumerateArray().Select(static t => new ReferenceArc200Transfer(
                t.GetProperty("contractId").GetUInt64(),
                new Address(new Algorand.Address(t.GetProperty("sender").GetString()!).Bytes),
                new Address(new Algorand.Address(t.GetProperty("receiver").GetString()!).Bytes),
                System.Numerics.BigInteger.Parse(t.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture))),
        ];
    }

    public void Dispose() => _http.Dispose();

    /// <summary>The <c>"Accts": { ... }</c> object of a delta document wrapped as a standalone JSON object.</summary>
    private static string LeadingAccts(string text)
    {
        var start = text.IndexOf("\"Accts\"", StringComparison.Ordinal);
        var open = start < 0 ? -1 : text.IndexOf('{', start);
        if (open < 0)
        {
            throw new InvalidDataException("Delta document has no Accts object.");
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                inString = c != '"' || escaped;
                escaped = !escaped && c == '\\';
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return "{\"Accts\":" + text[open..(i + 1)] + "}";
            }
        }

        throw new InvalidDataException("Delta document has no Accts object.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url) => JsonDocument.Parse(await GetTextAsync(url).ConfigureAwait(false));

    private async Task<string> GetTextAsync(string url)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(new Uri(url)).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
                if (!retryable || attempt >= RetryDelays.Length)
                {
                    throw new HttpRequestException($"GET {url} returned {(int)response.StatusCode}.");
                }
            }
            catch (HttpRequestException) when (attempt < RetryDelays.Length)
            {
            }
            catch (TaskCanceledException) when (attempt < RetryDelays.Length)
            {
            }

            await Task.Delay(RetryDelays[attempt]).ConfigureAwait(false);
        }
    }
}

/// <summary>The balance-relevant fields of a payment-like transaction as reported by the indexer.</summary>
/// <remarks>Inner transactions have no id and no confirmed round in the indexer response.</remarks>
public sealed record ReferenceTransaction(
    string Id,
    string Type,
    ulong Round,
    Address Sender,
    ulong Fee,
    Address? Receiver,
    ulong Amount,
    Address? CloseTo,
    ulong ClosingAmount,
    ulong SenderRewards,
    ulong ReceiverRewards,
    ulong CloseRewards)
{
    public static ReferenceTransaction From(JsonElement tx, bool inner = false)
    {
        static Address Parse(string text) => new(new Algorand.Address(text).Bytes);

        ulong Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetUInt64() : 0;

        var type = tx.GetProperty("tx-type").GetString()!;
        Address? receiver = null, closeTo = null;
        ulong amount = 0, closing = 0;
        if (tx.TryGetProperty("payment-transaction", out var payment))
        {
            receiver = Parse(payment.GetProperty("receiver").GetString()!);
            amount = Number(payment, "amount");
            closing = Number(payment, "close-amount");
            closeTo = payment.TryGetProperty("close-remainder-to", out var close) ? Parse(close.GetString()!) : null;
        }

        return new ReferenceTransaction(
            inner ? string.Empty : tx.GetProperty("id").GetString()!,
            type,
            inner ? 0 : tx.GetProperty("confirmed-round").GetUInt64(),
            Parse(tx.GetProperty("sender").GetString()!),
            Number(tx, "fee"),
            receiver,
            amount,
            closeTo,
            closing,
            Number(tx, "sender-rewards"),
            Number(tx, "receiver-rewards"),
            Number(tx, "close-rewards"));
    }
}

/// <summary>An ARC-200 transfer as reported by the ARC indexer; mints have the zero address as sender, burns as receiver.</summary>
public sealed record ReferenceArc200Transfer(ulong ContractId, Address Sender, Address Receiver, System.Numerics.BigInteger Amount);
