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
    /// <summary>A quota answer is one line of text; a longer body is something else and is not read.</summary>
    private const int LongestQuotaAnswer = 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Forbidden || response.Content.Headers.ContentLength is 0 or > LongestQuotaAnswer)
        {
            return response;
        }

        // Buffered, so that the caller can still read the body of a 403 that is not about the quota.
        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!body.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        response.Dispose();
        throw new NodeQuotaExceededException($"The node answered HTTP 403 for {request.RequestUri?.AbsolutePath}: {body.Trim()}");
    }
}
