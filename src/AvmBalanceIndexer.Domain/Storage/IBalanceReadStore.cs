using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Read side of the storage contract. All reads are bounded by the published indexed round
/// and must return identical results across providers for the same canonical effects.
/// </summary>
public interface IBalanceReadStore
{
    /// <summary>Current holdings of one account ordered by token kind and id.</summary>
    Task<Page<Holding>> GetHoldingsAsync(Address account, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>Current holdings for a set of account/token keys; keys without a row are absent from the result.</summary>
    Task<IReadOnlyDictionary<HoldingKey, Holding>> GetCurrentHoldingsAsync(IReadOnlyCollection<HoldingKey> keys, CancellationToken cancellationToken);

    /// <summary>End-of-round balance via the latest effect at or before the round.</summary>
    /// <exception cref="RoundNotIndexedException">The round is beyond the published cursor.</exception>
    Task<Amount> GetBalanceAtRoundAsync(Address account, TokenKey token, ulong round, CancellationToken cancellationToken);

    /// <summary>Holders of a token at the end of a round, ordered by account bytes.</summary>
    /// <exception cref="RoundNotIndexedException">The round is beyond the published cursor.</exception>
    Task<Page<Holding>> GetHoldersAtRoundAsync(TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>Effects of one account/token, newest first, within the optional inclusive round range.</summary>
    Task<Page<TokenEffect>> GetEffectsAsync(Address account, TokenKey token, ulong? fromRound, ulong? toRound, string? cursor, int limit, CancellationToken cancellationToken);
}
