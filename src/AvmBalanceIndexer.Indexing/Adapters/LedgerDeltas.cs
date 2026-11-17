using System.Net;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Chains;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// The balances the ledger itself holds after one round, for every account that round changed: the post-round state delta of the node.
/// </summary>
/// <param name="Algo">Native balances (microalgos) after the round.</param>
/// <param name="Assets">Asset holdings after the round; a null amount means the holding was removed (opt-out, destroy), which equals zero.</param>
public sealed record LedgerDelta(IReadOnlyDictionary<Address, ulong> Algo, IReadOnlyDictionary<(Address Account, ulong AssetId), ulong?> Assets)
{
    /// <summary>Reads a <c>/v2/deltas/{round}</c> document.</summary>
    /// <param name="text">The response body.</param>
    /// <exception cref="InvalidDataException">The document has no <c>Accts</c> object.</exception>
    /// <exception cref="JsonException">The document is malformed.</exception>
    public static LedgerDelta Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Later sections of recent deltas carry integer map keys, which is not valid JSON, so only the leading Accts value is parsed.
        using var json = JsonDocument.Parse(LeadingAccts(text));
        var accts = json.RootElement.GetProperty("Accts");
        var algo = new Dictionary<Address, ulong>();
        foreach (var account in accts.GetProperty("Accts").EnumerateArray())
        {
            algo[new Address(Convert.FromBase64String(account.GetProperty("Addr").GetString()!))] = account.GetProperty("MicroAlgos").GetUInt64();
        }

        // Holding.Deleted means the holding was removed; Holding.Holding is {} for a zero holding that exists (docs/ledger-facts.md).
        var assets = new Dictionary<(Address, ulong), ulong?>();
        if (accts.TryGetProperty("AssetResources", out var resources) && resources.ValueKind == JsonValueKind.Array)
        {
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
                assets[(address, resource.GetProperty("Aidx").GetUInt64())] = deleted ? null : data.TryGetProperty("a", out var amount) ? amount.GetUInt64() : 0UL;
            }
        }

        return new LedgerDelta(algo, assets);
    }

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
}

/// <summary>Where the ledger's own post-round balances come from (the ledger audit, ADR 0021).</summary>
public interface ILedgerDeltaSource
{
    /// <summary>The ledger delta of a round.</summary>
    /// <param name="round">The round.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Null when the node has no deltas for that round (404 or 501).</returns>
    /// <exception cref="HttpRequestException">The node did not answer (timeout, throttling, server error): not a statement about the round.</exception>
    /// <exception cref="InvalidDataException">The answer could not be read.</exception>
    Task<LedgerDelta?> TryGetAsync(ulong round, CancellationToken cancellationToken);
}

/// <summary>Reads ledger deltas from algod's <c>GET /v2/deltas/{round}</c>.</summary>
public sealed partial class AlgodLedgerDeltaSource(HttpClient http, ILogger<AlgodLedgerDeltaSource> logger, bool ownsClient = false) : ILedgerDeltaSource, IDisposable
{
    public async Task<LedgerDelta?> TryGetAsync(ulong round, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri($"v2/deltas/{round}", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented)
        {
            LogNoDeltas(logger, round, (int)response.StatusCode);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"algod answered {(int)response.StatusCode} for the deltas of round {round}", null, response.StatusCode);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return LedgerDelta.Parse(text);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or InvalidDataException)
        {
            throw new InvalidDataException($"The deltas of round {round} could not be read: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            http.Dispose();
        }
    }

    [LoggerMessage(EventId = 1140, Level = LogLevel.Debug, Message = "The node has no ledger deltas for round {Round} (HTTP {Status})")]
    private static partial void LogNoDeltas(ILogger logger, ulong round, int status);
}
