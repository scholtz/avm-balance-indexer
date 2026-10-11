using System.Globalization;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Api;

public sealed record TokenRef(string Kind, ulong TokenId);

public sealed record HoldingDto(TokenRef Token, string Amount, bool OptedIn, ulong LastChangedRound);

public sealed record HolderDto(string Account, string Amount, bool OptedIn, ulong LastChangedRound);

public sealed record EffectDto(
    ulong Round,
    int IntraRoundOrder,
    int Ordinal,
    string TransactionId,
    IReadOnlyList<int> InnerPath,
    string Type,
    string Delta,
    string BalanceAfter,
    string? Counterparty);

public sealed record HoldingsResponse(string Account, ulong IndexedRound, IReadOnlyList<HoldingDto> Items, string? NextCursor);

public sealed record BalanceResponse(string Account, TokenRef Token, ulong Round, string Amount, ulong IndexedRound);

public sealed record HoldersResponse(TokenRef Token, ulong Round, ulong IndexedRound, IReadOnlyList<HolderDto> Items, string? NextCursor);

public sealed record HistoryResponse(string Account, TokenRef Token, ulong IndexedRound, IReadOnlyList<EffectDto> Items, string? NextCursor);

public sealed record TokenResponse(
    TokenRef Token,
    string? Symbol,
    string? Name,
    int? Decimals,
    string Standard,
    bool StandardVerified,
    string? Creator,
    ulong? ControllingAppId,
    ulong? CreatedRound,
    ulong? DeletedRound);

/// <summary>
/// The read endpoints of /v1/{chain}. They only translate between HTTP and the storage contract: inputs are validated here,
/// every response carries the published round it is consistent with, amounts are decimal strings, and paging cursors are
/// passed through opaquely.
/// </summary>
public static class ReadEndpoints
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;
    private const int MaxCursorLength = 256;

    public static RouteGroupBuilder MapReadEndpoints(this RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        v1.MapGet("/accounts/{address}/tokens", GetHoldings)
            .WithName("GetAccountTokens")
            .WithTags("Balances")
            .WithSummary("Current token holdings of an account")
            .WithDescription("Holdings ordered by token kind and id. Zero balances (opt-ins) are listed with includeZero=true. Page with nextCursor.")
            .Produces<HoldingsResponse>()
            .WithErrors(StatusCodes.Status400BadRequest)
            .WithCommonErrors();
        v1.MapGet("/accounts/{address}/tokens/{kind}/{tokenId}/balance", GetBalance)
            .WithName("GetTokenBalance")
            .WithTags("Balances")
            .WithSummary("End-of-round balance of an account")
            .WithDescription("The balance after the last effect at or before the round (default: the latest published round), including rounds without activity. An explicit published round is cacheable.")
            .Produces<BalanceResponse>()
            .WithErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound)
            .WithCommonErrors();
        v1.MapGet("/accounts/{address}/tokens/{kind}/{tokenId}/transactions", GetHistory)
            .WithName("GetTokenHistory")
            .WithTags("History")
            .WithSummary("Balance effects of an account and token, newest first")
            .WithDescription("Every change of the balance with its signed delta, the balance after it and the transaction it came from. Limit the range with fromRound and toRound.")
            .Produces<HistoryResponse>()
            .WithErrors(StatusCodes.Status400BadRequest)
            .WithCommonErrors();
        v1.MapGet("/tokens/{kind}/{tokenId}/holders", GetHolders)
            .WithName("GetTokenHolders")
            .WithTags("Holders")
            .WithSummary("Holders of a token at the end of a round")
            .WithDescription("Holders in account order. For a very large token at a past round use an asynchronous scan. Pass the same round while paging.")
            .Produces<HoldersResponse>()
            .WithErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound)
            .WithCommonErrors();
        v1.MapGet("/tokens/{kind}/{tokenId}", GetToken)
            .WithName("GetToken")
            .WithTags("Tokens")
            .WithSummary("Token metadata")
            .Produces<TokenResponse>()
            .WithErrors(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound)
            .WithCommonErrors();
        return v1;
    }

    private static async Task<IResult> GetHoldings(
        string address,
        IChainStorage storage,
        string? includeZero,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken)
    {
        if (!TryAccount(address, out var account, out var error) || !TryFlag(includeZero, "includeZero", out var withZero, out error)
            || !TryLimit(limit, out var pageSize, out error) || !TryCursor(cursor, out error))
        {
            return error!;
        }

        var page = await storage.GetHoldingsAsync(account, withZero, cursor, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new HoldingsResponse(
            AddressText.Format(account),
            page.IndexedRound,
            [.. page.Items.Select(static h => new HoldingDto(Ref(h.Token), h.Amount.ToString(), h.OptedIn, h.LastChangedRound))],
            page.NextCursor));
    }

    private static async Task<IResult> GetBalance(
        HttpContext http,
        ChainContext chain,
        string address,
        string kind,
        string tokenId,
        IChainStorage storage,
        string? round,
        CancellationToken cancellationToken)
    {
        if (!TryAccount(address, out var account, out var error) || !TryToken(kind, tokenId, out var token, out error) || !TryRound(round, out var requested, out error))
        {
            return error!;
        }

        var indexed = await IndexedRoundAsync(storage, chain, cancellationToken).ConfigureAwait(false);
        var at = requested ?? indexed;
        var amount = await storage.GetBalanceAtRoundAsync(account, token, at, cancellationToken).ConfigureAwait(false);
        MarkImmutable(http, requested);
        return Results.Ok(new BalanceResponse(AddressText.Format(account), Ref(token), at, amount.ToString(), indexed));
    }

    private static async Task<IResult> GetHolders(
        HttpContext http,
        ChainContext chain,
        string kind,
        string tokenId,
        IChainStorage storage,
        string? round,
        string? includeZero,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken)
    {
        if (!TryToken(kind, tokenId, out var token, out var error) || !TryRound(round, out var requested, out error)
            || !TryFlag(includeZero, "includeZero", out var withZero, out error)
            || !TryLimit(limit, out var pageSize, out error) || !TryCursor(cursor, out error))
        {
            return error!;
        }

        var at = requested ?? await IndexedRoundAsync(storage, chain, cancellationToken).ConfigureAwait(false);
        var page = await storage.GetHoldersAtRoundAsync(token, at, withZero, cursor, pageSize, cancellationToken).ConfigureAwait(false);
        MarkImmutable(http, requested);
        return Results.Ok(new HoldersResponse(
            Ref(token),
            at,
            page.IndexedRound,
            [.. page.Items.Select(static h => new HolderDto(AddressText.Format(h.Account), h.Amount.ToString(), h.OptedIn, h.LastChangedRound))],
            page.NextCursor));
    }

    private static async Task<IResult> GetHistory(
        HttpContext http,
        string address,
        string kind,
        string tokenId,
        IChainStorage storage,
        string? fromRound,
        string? toRound,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken)
    {
        if (!TryAccount(address, out var account, out var error) || !TryToken(kind, tokenId, out var token, out error)
            || !TryRound(fromRound, out var from, out error, "fromRound") || !TryRound(toRound, out var to, out error, "toRound")
            || !TryLimit(limit, out var pageSize, out error) || !TryCursor(cursor, out error))
        {
            return error!;
        }

        if (from > to)
        {
            return BadRequest("fromRound must not be greater than toRound.");
        }

        var page = await storage.GetEffectsAsync(account, token, from, to, cursor, pageSize, cancellationToken).ConfigureAwait(false);
        if (to is { } upper && upper <= page.IndexedRound)
        {
            // A window that ends at a published round cannot change any more.
            MarkImmutable(http, upper);
        }

        return Results.Ok(new HistoryResponse(AddressText.Format(account), Ref(token), page.IndexedRound, [.. page.Items.Select(ToDto)], page.NextCursor));
    }

    private static async Task<IResult> GetToken(string kind, string tokenId, IChainStorage storage, CancellationToken cancellationToken)
    {
        if (!TryToken(kind, tokenId, out var token, out var error))
        {
            return error!;
        }

        var info = await storage.GetAsync(token, cancellationToken).ConfigureAwait(false);
        return info is null
            ? Results.NotFound(new { error = "The token is not known.", token = Ref(token) })
            : Results.Ok(new TokenResponse(
                Ref(info.Key),
                info.Symbol,
                info.Name,
                info.Decimals,
                info.Standard.ToString(),
                info.StandardVerified,
                info.Creator is { } creator ? AddressText.Format(creator) : null,
                info.ControllingAppId,
                info.CreatedRound,
                info.DeletedRound));
    }

    private static EffectDto ToDto(TokenEffect e) => new(
        e.Id.Round,
        e.Id.IntraRoundOrder,
        e.Id.Ordinal,
        e.Id.TransactionId,
        e.Id.InnerPath,
        e.Type.ToString(),
        e.Delta.ToString(CultureInfo.InvariantCulture),
        e.BalanceAfter.ToString(),
        e.Counterparty is { } counterparty ? AddressText.Format(counterparty) : null);

    private static TokenRef Ref(TokenKey token) => new(token.Kind.ToStorageName(), token.TokenId);

    /// <summary>
    /// A round that was asked for explicitly and is published has a fixed answer, so clients and proxies may keep it for a while.
    /// Not <c>immutable</c>: <c>indexedRound</c> in the body is as of generation time, and an operator may have to reindex.
    /// </summary>
    private static void MarkImmutable(HttpContext http, ulong? requestedRound)
    {
        if (requestedRound is not null)
        {
            http.Response.Headers.CacheControl = "public, max-age=3600";
        }
    }

    private static async Task<ulong> IndexedRoundAsync(IChainStorage storage, ChainContext chain, CancellationToken cancellationToken) =>
        (await storage.GetAsync(chain.Chain, cancellationToken).ConfigureAwait(false))?.IndexedRound
        ?? throw new ChainNotStartedException(chain.Chain);

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

    private static bool TryAccount(string text, out Address account, out IResult? error)
    {
        error = AddressText.TryParse(text, out account) ? null : BadRequest("The address is not a valid Algorand address.");
        return error is null;
    }

    internal static bool TryToken(string kind, string tokenId, out TokenKey token, out IResult? error)
    {
        token = default;
        error = null;
        if (!TokenKindExtensions.TryParseStorageName(kind, out var parsedKind))
        {
            error = BadRequest("The token kind must be algo, asa or arc200.");
        }
        else if (!TryCanonical(tokenId, out var id) || id > long.MaxValue)
        {
            error = BadRequest("The token id must be a non-negative integer below 2^63.");
        }
        else if (parsedKind == TokenKind.Algo && id != 0)
        {
            error = BadRequest("The native token has id 0.");
        }
        else
        {
            token = new TokenKey(parsedKind, id);
        }

        return error is null;
    }

    private static bool TryRound(string? text, out ulong? round, out IResult? error, string name = "round")
    {
        round = null;
        error = null;
        if (text is null)
        {
            return true;
        }

        if (TryCanonical(text, out var parsed))
        {
            round = parsed;
        }
        else
        {
            error = BadRequest($"{name} must be a non-negative integer.");
        }

        return error is null;
    }

    internal static bool TryLimit(string? text, out int pageSize, out IResult? error)
    {
        pageSize = DefaultLimit;
        if (text is not null)
        {
            pageSize = TryCanonical(text, out var parsed) && parsed <= int.MaxValue ? (int)parsed : 0;
        }

        error = pageSize is >= 1 and <= MaxLimit ? null : BadRequest($"limit must be between 1 and {MaxLimit}.");
        return error is null;
    }

    /// <summary>Flags are parsed here, not by parameter binding, so a bad query on another chain is still the chain filter's 404.</summary>
    private static bool TryFlag(string? text, string name, out bool value, out IResult? error)
    {
        value = false;
        if (text is null || text is "true" or "false")
        {
            value = text is "true";
            error = null;
        }
        else
        {
            error = BadRequest($"{name} must be true or false.");
        }

        return error is null;
    }

    /// <summary>Digits without leading zeros, so each value has one spelling (and one cache key).</summary>
    private static bool TryCanonical(string text, out ulong value) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && string.Equals(value.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal);

    private static bool TryCursor(string? cursor, out IResult? error)
    {
        error = cursor is { Length: > MaxCursorLength } ? BadRequest("The cursor is not valid.") : null;
        return error is null;
    }
}
