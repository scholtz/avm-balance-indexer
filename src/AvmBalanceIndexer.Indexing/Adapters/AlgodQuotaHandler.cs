using System.Net;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// The node refused a request because the daily quota of its public gateway is used up. Nothing is wrong with the request or the node:
/// the quota is renewed by itself (Nodely: at 00:00 UTC), and every refused request counts against it too.
/// </summary>
public sealed class NodeQuotaExceededException(string message) : HttpRequestException(message, null, HttpStatusCode.Forbidden);

/// <summary>
/// Turns the quota answer of a public algod gateway into <see cref="NodeQuotaExceededException"/> for every caller of the algod client
/// (status, genesis, ledger deltas, application state), so that all of them can tell it from a 403 that is a blocked address or a bad token.
/// The answer is HTTP 403 with a short text that names the quota (Nodely: "Daily free API quota exceeded", https://nodely.io/docs/free/policy).
/// </summary>
internal sealed class AlgodQuotaHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    /// <summary>A quota answer is one line of text; a longer body is something else.</summary>
    private const int LongestQuotaAnswer = 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Forbidden || response.Content.Headers.ContentLength is 0 or > LongestQuotaAnswer)
        {
            return response;
        }

        // At most LongestQuotaAnswer + 1 bytes are read, also of a body without a length (chunked, decompressed). The caller gets what
        // was read back as the body: whole for a short 403, cut to that length for a longer one (an HTML page of a firewall).
        var buffer = new byte[LongestQuotaAnswer + 1];
        var read = 0;
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0)
            {
                read += n;
            }
        }

        var body = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        if (read > LongestQuotaAnswer || !body.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            var original = response.Content;
            response.Content = new ByteArrayContent(buffer, 0, read);
            response.Content.Headers.ContentType = original.Headers.ContentType;
            original.Dispose();
            return response;
        }

        response.Dispose();
        throw new NodeQuotaExceededException($"The node answered HTTP 403 for {request.RequestUri?.AbsolutePath}: {body.Trim()}");
    }
}
