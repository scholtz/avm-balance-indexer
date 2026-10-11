using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Api;

public sealed record ScanRequest(ulong? Round, bool? IncludeZero);

public sealed record ScanResponse(
    Guid Id,
    string State,
    TokenRef Token,
    ulong Round,
    bool IncludeZero,
    long HoldersFound,
    bool Complete,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ScanHoldersResponse(Guid Id, string State, bool Complete, ulong Round, IReadOnlyList<HolderDto> Items, string? NextCursor);

/// <summary>
/// Asynchronous holder scans: a consistent export of every holder of a token at a round that is too large to page synchronously.
/// The request is accepted at once, a worker replica fills the result, and the requester polls the job and reads the result pages
/// (also while it is still being filled: pages only ever grow at the end).
/// </summary>
public static class ScanEndpoints
{
    public static RouteGroupBuilder MapScanEndpoints(this RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        v1.MapPost("/tokens/{kind}/{tokenId}/holders/scans", Create)
            .WithName("CreateHolderScan")
            .WithTags("Scans")
            .WithSummary("Start an asynchronous export of every holder of a token at a round")
            .WithDescription("Accepted at once; poll the scan and read the result pages. The body is optional: { \"round\": 123, \"includeZero\": false }.")
            .Accepts<ScanRequest>(true, "application/json")
            .Produces<ScanResponse>(StatusCodes.Status202Accepted)
            .WithErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound)
            .WithCommonErrors();
        v1.MapGet("/scans/{id}", GetJob)
            .WithName("GetHolderScan")
            .WithTags("Scans")
            .WithSummary("State and progress of a scan")
            .Produces<ScanResponse>()
            .WithErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound)
            .WithCommonErrors();
        v1.MapGet("/scans/{id}/holders", GetHolders)
            .WithName("GetHolderScanResult")
            .WithTags("Scans")
            .WithSummary("Result pages of a scan (also while it is running)")
            .WithDescription("Without a nextCursor and with complete=false, repeat the same request a moment later.")
            .Produces<ScanHoldersResponse>()
            .WithErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound)
            .WithCommonErrors();
        v1.MapDelete("/scans/{id}", Cancel)
            .WithName("CancelHolderScan")
            .WithTags("Scans")
            .WithSummary("Cancel a scan")
            .Produces<ScanResponse>()
            .WithErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithCommonErrors();
        return v1;
    }

    private static async Task<IResult> Create(
        HttpContext http,
        ChainContext chain,
        string kind,
        string tokenId,
        ScanRequest? request,
        IChainStorage storage,
        IScanJobStore jobs,
        IOptions<ScanOptions> options,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!ReadEndpoints.TryToken(kind, tokenId, out var token, out var error))
        {
            return error!;
        }

        if (await storage.GetAsync(token, cancellationToken).ConfigureAwait(false) is null)
        {
            return Results.NotFound(new { error = "The token is not known.", token = new TokenRef(token.Kind.ToStorageName(), token.TokenId) });
        }

        var settings = options.Value;
        var now = clock.GetUtcNow();
        var indexed = (await storage.GetAsync(chain.Chain, cancellationToken).ConfigureAwait(false))?.IndexedRound
            ?? throw new ChainNotStartedException(chain.Chain);
        var round = request?.Round ?? indexed;
        if (round > indexed)
        {
            throw new RoundNotIndexedException(round, indexed);
        }

        if (await jobs.CountActiveAsync(now, cancellationToken).ConfigureAwait(false) >= settings.MaxActive)
        {
            http.Response.Headers.RetryAfter = "60";
            return Results.Json(new { error = "Too many scans are running; try again later." }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var job = new ScanJob(Guid.NewGuid(), token, round, request?.IncludeZero ?? false, ScanState.Pending, 0, null, null, now, now, now + settings.Retention);
        await jobs.CreateAsync(job, cancellationToken).ConfigureAwait(false);
        return Results.Accepted($"/v1/{chain.Chain}/scans/{job.Id}", ToResponse(job));
    }

    private static async Task<IResult> GetJob(string id, IScanJobStore jobs, CancellationToken cancellationToken)
    {
        if (!TryId(id, out var parsed))
        {
            return Results.BadRequest(new { error = "The scan id is not valid." });
        }

        return await jobs.GetJobAsync(parsed, cancellationToken).ConfigureAwait(false) is { } job
            ? Results.Ok(ToResponse(job))
            : NotFound();
    }

    private static async Task<IResult> GetHolders(string id, IScanJobStore jobs, string? cursor, string? limit, CancellationToken cancellationToken)
    {
        if (!TryId(id, out var parsed))
        {
            return Results.BadRequest(new { error = "The scan id is not valid." });
        }

        if (!ReadEndpoints.TryLimit(limit, out var pageSize, out var limitError))
        {
            return limitError!;
        }

        if (cursor is { Length: > 32 })
        {
            return Results.BadRequest(new { error = "The cursor is not valid." });
        }

        if (await jobs.GetJobAsync(parsed, cancellationToken).ConfigureAwait(false) is not { } job)
        {
            return NotFound();
        }

        var page = await jobs.GetResultAsync(parsed, cursor, pageSize, cancellationToken).ConfigureAwait(false);

        // A cursor that ends a page of a scan that is still running means "caught up so far", not "finished".
        return Results.Ok(new ScanHoldersResponse(
            job.Id,
            job.State.ToString(),
            job.State == ScanState.Completed && page.NextCursor is null,
            job.Round,
            [.. page.Items.Select(static h => new HolderDto(AddressText.Format(h.Account), h.Amount.ToString(), h.OptedIn, h.LastChangedRound))],
            page.NextCursor));
    }

    private static async Task<IResult> Cancel(string id, IScanJobStore jobs, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!TryId(id, out var parsed))
        {
            return Results.BadRequest(new { error = "The scan id is not valid." });
        }

        if (await jobs.GetJobAsync(parsed, cancellationToken).ConfigureAwait(false) is not { } existing)
        {
            return NotFound();
        }

        // Cancelling a scan that already finished is not an error; the response shows what state it is in.
        await jobs.CancelAsync(parsed, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var job = await jobs.GetJobAsync(parsed, cancellationToken).ConfigureAwait(false) ?? existing;
        return job.IsFinished
            ? Results.Ok(ToResponse(job))
            : Results.Json(new { error = "The scan could not be cancelled right now; try again.", scan = ToResponse(job) }, statusCode: StatusCodes.Status409Conflict);
    }

    private static bool TryId(string text, out Guid id) =>
        Guid.TryParseExact(text, "D", out id);

    private static IResult NotFound() => Results.NotFound(new { error = "The scan is not known (it may have expired)." });

    private static ScanResponse ToResponse(ScanJob job) => new(
        job.Id,
        job.State.ToString(),
        new TokenRef(job.Token.Kind.ToStorageName(), job.Token.TokenId),
        job.Round,
        job.IncludeZero,
        job.HoldersFound,
        job.State == ScanState.Completed,
        job.Error,
        job.CreatedAt,
        job.UpdatedAt,
        job.ExpiresAt);
}
