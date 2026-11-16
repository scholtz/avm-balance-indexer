using System.Buffers;
using System.Net;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Normalization;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Reads ARC-200 metadata through algod's <c>simulate</c>: one request per read-only method (algod simulates one transaction group
/// per request, and one failing method must not fail the others). Every transport problem gives null so that indexing never waits for it.
/// </summary>
public sealed partial class AlgodArc200MetadataSource(HttpClient http, ReadOnlyMemory<byte> genesisHash, ILogger<AlgodArc200MetadataSource> logger, bool ownsClient = false)
    : IArc200MetadataSource, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private const ulong ValidityWindow = 100;
    private const ulong FirstValidSlack = 5;

    /// <summary>Disposes the HTTP client when this source created it.</summary>
    public void Dispose()
    {
        if (ownsClient)
        {
            http.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<Arc200Metadata?> TryReadAsync(ulong applicationId, ReadOnlyMemory<byte> sender, ulong latestRound, CancellationToken cancellationToken)
    {
        try
        {
            var answers = await Task.WhenAll(Arc200Metadata.Methods.Select(method =>
                CallAsync(applicationId, Arc200Metadata.Selector(method.Signature), sender, latestRound, cancellationToken))).ConfigureAwait(false);

            string? name = Arc200Metadata.TryDecodeText(answers[0], Arc200Metadata.Methods[0].Size, out var n) ? n : null;
            string? symbol = Arc200Metadata.TryDecodeText(answers[1], Arc200Metadata.Methods[1].Size, out var s) ? s : null;
            int? decimals = Arc200Metadata.TryDecodeUint(answers[2], Arc200Metadata.Methods[2].Size, out var d) ? (int)d : null;
            Amount? supply = Arc200Metadata.TryDecodeUint(answers[3], Arc200Metadata.Methods[3].Size, out var t) ? new Amount(t) : null;
            return new Arc200Metadata(name, symbol, decimals, supply);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogLookupFailed(logger, applicationId, ex.Message);
            return null;
        }
    }

    /// <summary>The log the call returned (the last one), or an empty array when the call failed (the application lacks the method).</summary>
    private async Task<byte[]> CallAsync(ulong applicationId, byte[] selector, ReadOnlyMemory<byte> sender, ulong latestRound, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var content = new ByteArrayContent(BuildRequest(applicationId, selector, sender.Span, latestRound, genesisHash.Span));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/msgpack");
        using var response = await http.PostAsync(new Uri("v2/transactions/simulate", UriKind.Relative), content, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Only 400 (a call that cannot be simulated) is the application's answer. Everything else (a wrong token 401, a gateway
            // 404, throttling, a server error) says nothing about the application and must not make every token look empty.
            return response.StatusCode == HttpStatusCode.BadRequest
                ? []
                : throw new HttpRequestException($"algod answered {(int)response.StatusCode}", null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var group = document.RootElement.GetProperty("txn-groups")[0];
        if (group.TryGetProperty("failure-message", out var failure) && failure.ValueKind == JsonValueKind.String)
        {
            return [];
        }

        var result = group.GetProperty("txn-results")[0].GetProperty("txn-result");
        if (!result.TryGetProperty("logs", out var logs) || logs.ValueKind != JsonValueKind.Array || logs.GetArrayLength() == 0)
        {
            return [];
        }

        return Convert.FromBase64String(logs[logs.GetArrayLength() - 1].GetString() ?? string.Empty);
    }

    /// <summary>The msgpack body of a simulate request (canonical: sorted keys, zero values omitted), exposed for tests.</summary>
    /// <param name="applicationId">The called application.</param>
    /// <param name="selector">The 4-byte method selector.</param>
    /// <param name="sender">The simulated sender.</param>
    /// <param name="latestRound">The first valid round.</param>
    /// <param name="genesisHash">The 32-byte genesis hash.</param>
    public static byte[] BuildRequest(ulong applicationId, ReadOnlySpan<byte> selector, ReadOnlySpan<byte> sender, ulong latestRound, ReadOnlySpan<byte> genesisHash)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        writer.Write("allow-empty-signatures");
        writer.Write(true);
        writer.Write("allow-unnamed-resources");
        writer.Write(true);
        writer.Write("txn-groups");
        writer.WriteArrayHeader(1);
        writer.WriteMapHeader(1);
        writer.Write("txns");
        writer.WriteArrayHeader(1);
        writer.WriteMapHeader(1);
        writer.Write("txn");
        writer.WriteMapHeader(8);
        writer.Write("apaa");
        writer.WriteArrayHeader(1);
        writer.Write(selector);
        writer.Write("apid");
        writer.Write(applicationId);
        writer.Write("fee");
        writer.Write(1000UL);
        writer.Write("fv");
        writer.Write(latestRound > FirstValidSlack ? latestRound - FirstValidSlack : 1); // a node a round or two behind must still accept it
        writer.Write("gh");
        writer.Write(genesisHash);
        writer.Write("lv");
        writer.Write((latestRound > FirstValidSlack ? latestRound - FirstValidSlack : 1) + ValidityWindow);
        writer.Write("snd");
        writer.Write(sender);
        writer.Write("type");
        writer.Write("appl");
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    [LoggerMessage(EventId = 1130, Level = LogLevel.Debug, Message = "ARC-200 metadata of application {ApplicationId} could not be read now: {Reason}")]
    private static partial void LogLookupFailed(ILogger logger, ulong applicationId, string reason);
}
