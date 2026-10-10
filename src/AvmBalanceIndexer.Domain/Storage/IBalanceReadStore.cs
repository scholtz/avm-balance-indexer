using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Read side of the storage contract. All reads are bounded by the published <see cref="ChainStatus.IndexedRound"/>
/// and must return identical results across providers for the same canonical effects.
/// </summary>
public interface IBalanceReadStore
{
    Task<Page<Holding>> GetHoldingsAsync(Address account, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>End-of-round balance via the latest effect at or before <paramref name="round"/>.</summary>
    Task<Amount> GetBalanceAtRoundAsync(Address account, TokenKey token, ulong round, CancellationToken cancellationToken);

    Task<Page<Holding>> GetHoldersAtRoundAsync(TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken);

    Task<Page<TokenEffect>> GetEffectsAsync(Address account, TokenKey token, ulong? fromRound, ulong? toRound, string? cursor, int limit, CancellationToken cancellationToken);
}
