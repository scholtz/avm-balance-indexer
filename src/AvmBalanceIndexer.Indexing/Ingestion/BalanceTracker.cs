using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>The effects of one round together with the balances to cache once the round is durably written.</summary>
public sealed record RoundEffects(IReadOnlyList<TokenEffect> Effects, IReadOnlyDictionary<HoldingKey, Amount> FinalBalances);

/// <summary>
/// Computes the exact running balance after every delta. Balances are read through from storage once and cached;
/// cache updates are applied only by <see cref="Commit"/> after the round was written, so a failed write never
/// poisons the cache. The cache is bounded and simply cleared when full, because storage is always the source of truth.
/// </summary>
public sealed class BalanceTracker(IBalanceReadStore store, int maxCacheEntries)
{
    private readonly Dictionary<HoldingKey, Amount> _cache = [];

    /// <summary>Applies deltas in order and returns the resulting effects.</summary>
    /// <exception cref="InvalidOperationException">A delta would make a balance negative.</exception>
    public async Task<RoundEffects> ApplyAsync(IReadOnlyList<BalanceDelta> deltas, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deltas);

        var keys = deltas.Select(static d => new HoldingKey(d.Account, d.Token)).Distinct().ToArray();
        var missing = keys.Where(k => !_cache.ContainsKey(k)).ToArray();
        var loaded = missing.Length == 0
            ? new Dictionary<HoldingKey, Holding>()
            : (await store.GetCurrentHoldingsAsync(missing, cancellationToken).ConfigureAwait(false)).ToDictionary();

        var working = new Dictionary<HoldingKey, Amount>(keys.Length);
        foreach (var key in keys)
        {
            working[key] = _cache.TryGetValue(key, out var cached)
                ? cached
                : loaded.TryGetValue(key, out var holding) ? holding.Amount : Amount.Zero;
        }

        var effects = new List<TokenEffect>(deltas.Count);
        foreach (var delta in deltas)
        {
            var key = new HoldingKey(delta.Account, delta.Token);
            var after = working[key].Apply(delta.Delta);
            working[key] = after;
            effects.Add(TokenEffect.From(delta, after));
        }

        return new RoundEffects(effects, working);
    }

    /// <summary>Records the balances of a round that was durably written.</summary>
    public void Commit(RoundEffects round)
    {
        ArgumentNullException.ThrowIfNull(round);

        if (_cache.Count + round.FinalBalances.Count > maxCacheEntries)
        {
            _cache.Clear();
        }

        foreach (var (key, amount) in round.FinalBalances)
        {
            _cache[key] = amount;
        }
    }
}
