using System.Numerics;
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
    private static readonly BigInteger Uint256Max = (BigInteger.One << 256) - 1;

    // Balances of rounds that are computed but not yet written; the next round must see them, the cache must not hold them yet.
    private readonly Dictionary<HoldingKey, Amount> _staged = [];
    private Dictionary<HoldingKey, Amount> _young = [];
    private Dictionary<HoldingKey, Amount> _old = [];

    /// <summary>Applies deltas in order and returns the resulting effects.</summary>
    /// <exception cref="InvalidOperationException">A native or ASA delta would make a balance negative.</exception>
    public async Task<RoundEffects> ApplyAsync(IReadOnlyList<BalanceDelta> deltas, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deltas);

        var keys = deltas.Select(static d => new HoldingKey(d.Account, d.Token)).Distinct().ToArray();
        var working = new Dictionary<HoldingKey, Amount>(keys.Length);
        var missing = new List<HoldingKey>();
        foreach (var key in keys)
        {
            if (_staged.TryGetValue(key, out var cached) || _young.TryGetValue(key, out cached) || _old.TryGetValue(key, out cached))
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
        BigInteger? creditLimit = null;
        for (var index = 0; index < deltas.Count; index++)
        {
            var delta = deltas[index];
            var key = new HoldingKey(delta.Account, delta.Token);
            var balance = working[key];
            var applied = delta;

            if (creditLimit is { } limit)
            {
                // The credit half of a transfer whose debit was limited moves the same, smaller amount.
                applied = delta with { Delta = limit };
                creditLimit = null;
            }
            else if (delta.Token.Kind == TokenKind.Arc200 && balance.Value + delta.Delta < BigInteger.Zero)
            {
                // ARC-200 balances are derived from events the application chooses to log. An application that moves tokens
                // without logging (or a forged log) must not stop the indexer, so a debit is limited to what is known.
                applied = delta with { Delta = -balance.Value };
                if (index + 1 < deltas.Count && IsCreditOf(delta, deltas[index + 1]))
                {
                    creditLimit = balance.Value;
                }
            }

            if (delta.Token.Kind == TokenKind.Arc200 && balance.Value + applied.Delta > Uint256Max)
            {
                // A balance never exceeds uint256, which is also what the database columns can hold.
                applied = applied with { Delta = Uint256Max - balance.Value };
            }

            if (applied.Delta.IsZero && applied != delta)
            {
                continue;
            }

            var after = balance.Apply(applied.Delta);
            working[key] = after;
            effects.Add(TokenEffect.From(applied, after));
        }

        return new RoundEffects(effects, working);
    }

    private static bool IsCreditOf(BalanceDelta debit, BalanceDelta next) =>
        debit.Type == EffectType.Arc200Transfer
        && next.Type == EffectType.Arc200Transfer
        && next.Token == debit.Token
        && next.Delta == -debit.Delta
        && next.Account == debit.Counterparty
        && next.Id.Round == debit.Id.Round
        && next.Id.IntraRoundOrder == debit.Id.IntraRoundOrder
        && next.Id.InnerPath.SequenceEqual(debit.Id.InnerPath);

    /// <summary>
    /// Remembers the balances of a computed round that is waiting for a batched write, so the next round starts from them.
    /// <see cref="CommitStaged"/> moves them to the cache once the batch is durable. When a write fails, the tracker must be
    /// discarded: the staged balances describe rounds that are not in storage.
    /// </summary>
    public void Stage(RoundEffects round)
    {
        ArgumentNullException.ThrowIfNull(round);
        foreach (var (key, amount) in round.FinalBalances)
        {
            _staged[key] = amount;
        }
    }

    /// <summary>
    /// Records the staged balances after the batch that contained them was durably written. A batch larger than a generation
    /// keeps its most recent entries instead of being dropped altogether.
    /// </summary>
    public void CommitStaged()
    {
        foreach (var (key, amount) in _staged)
        {
            _young[key] = amount;
            _old.Remove(key);
            if (_young.Count >= _generationSize)
            {
                _old = _young;
                _young = [];
            }
        }

        _staged.Clear();
    }

    /// <summary>Records the balances of a round that was durably written.</summary>
    public void Commit(RoundEffects round)
    {
        ArgumentNullException.ThrowIfNull(round);

        var balances = round.FinalBalances;
        if (balances.Count >= _generationSize)
        {
            // A round this large would not fit a generation. Forget what it changed instead of caching it; storage has it.
            foreach (var (key, _) in balances)
            {
                _young.Remove(key);
                _old.Remove(key);
            }

            return;
        }

        foreach (var (key, amount) in balances)
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
