using AvmBalanceIndexer.Domain.Storage;
using Microsoft.AspNetCore.Diagnostics;

namespace AvmBalanceIndexer.Api;

/// <summary>
/// Turns the storage contract's expected failures into clear client responses instead of generic 500s: a chain nobody has
/// indexed yet, a round beyond the cursor, and malformed paging cursors. Anything else stays an internal error.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (Map(exception) is not var (status, body))
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(body, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The status code and JSON body for an expected failure, or null for an unexpected one.</summary>
    public static (int Status, object Body)? Map(Exception exception) => exception switch
    {
        ChainNotStartedException e => (StatusCodes.Status503ServiceUnavailable, new { error = "The chain has not been indexed yet.", chain = e.Chain }),
        RoundNotIndexedException e => (StatusCodes.Status404NotFound, new { error = "The round is not indexed yet.", requestedRound = e.RequestedRound, indexedRound = e.IndexedRound }),
        ArgumentException { ParamName: "cursor" } => (StatusCodes.Status400BadRequest, new { error = "The cursor is not valid." }),
        ArgumentOutOfRangeException { ParamName: "tokenId" } => (StatusCodes.Status400BadRequest, new { error = "The token id is not supported." }),
        _ => null,
    };
}
