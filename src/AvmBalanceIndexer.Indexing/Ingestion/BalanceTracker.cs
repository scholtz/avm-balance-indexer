using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>The effects of one round together with the balances to cache once the round is durably written.</summary>
public sealed record RoundEffects(IReadOnlyList<TokenEffect> Effects, IReadOnlyDictionary<HoldingKey, Amount> FinalBalances);

/// <summary>
/// Computes the exact running balance after every delta. Balances are read through from storage once and cached;
/// cache updates are applied only by <see cref="Commit"/> after the round was written, so a failed write never
/// poisons the cache. The cache keeps two generations of at most half the configured size each: when the young one is
/// full it becomes the old one and the previous old one is dropped. Recently used accounts therefore survive, there is no
/// periodic cliff where everything is forgotten, and storage stays the source of truth for anything evicted.
/// </summary>
public sealed class BalanceTracker(IBalanceReadStore store, int maxCacheEntries)
{
    private readonly int _generationSize = Math.Max(1, maxCacheEntries / 2);
    private Dictionary<HoldingKey, Amount> _young = [];
    private Dictionary<HoldingKey, Amount> _old = [];

    /// <summary>Applies deltas in order and returns the resulting effects.</summary>
    /// <exception cref="InvalidOperationException">A delta would make a balance negative.</exception>
    public async Task<RoundEffects> ApplyAsync(IReadOnlyList<BalanceDelta> deltas, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deltas);

        var keys = deltas.Select(static d => new HoldingKey(d.Account, d.Token)).Distinct().ToArray();
        var working = new Dictionary<HoldingKey, Amount>(keys.Length);
        var missing = new List<HoldingKey>();
        foreach (var key in keys)
        {
            if (_young.TryGetValue(key, out var cached) || _old.TryGetValue(key, out cached))
            {
                working[key] = cached;
            }
            else
            {
                missing.Add(key);
            }
        }

        if (missing.Count > 0)
        {
            var loaded = await store.GetCurrentHoldingsAsync(missing, cancellationToken).ConfigureAwait(false);
            foreach (var key in missing)
            {
                working[key] = loaded.TryGetValue(key, out var holding) ? holding.Amount : Amount.Zero;
            }
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

        foreach (var (key, amount) in round.FinalBalances)
        {
            _young[key] = amount;
            _old.Remove(key);
        }

        if (_young.Count >= _generationSize)
        {
            _old = _young;
            _young = [];
        }
    }
}
