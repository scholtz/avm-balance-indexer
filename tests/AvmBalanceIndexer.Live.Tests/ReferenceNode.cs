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

    public ReferenceNode(string algodUrl, string indexerUrl)
    {
        _algod = algodUrl.TrimEnd('/');
        _indexer = indexerUrl.TrimEnd('/');
    }

    public static ReferenceNode TestNet() => new("https://testnet-api.4160.nodely.dev", "https://testnet-idx.4160.nodely.dev");

    public static ReferenceNode Voi() => new("https://mainnet-api.voi.nodely.dev", "https://mainnet-idx.voi.nodely.dev");

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
            result.AddRange(root.GetProperty("transactions").EnumerateArray().Select(ReferenceTransaction.From));
            next = root.TryGetProperty("next-token", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : null;
        }
        while (next is not null);

        return result;
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
            result[address] = account.TryGetProperty("MicroAlgos", out var micro) ? micro.GetUInt64() : 0;
        }

        return result;
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
    public static ReferenceTransaction From(JsonElement tx)
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
            tx.GetProperty("id").GetString()!,
            type,
            tx.GetProperty("confirmed-round").GetUInt64(),
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
