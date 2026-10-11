using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>One round to persist: its cursor and all of its effects.</summary>
/// <param name="Status">Cursor after the round (round number, block hash, target, state).</param>
/// <param name="Effects">Every effect of the round, in ledger order.</param>
public sealed record RoundWrite(ChainStatus Status, IReadOnlyList<TokenEffect> Effects)
{
    /// <summary>
    /// Checks what every provider requires of a batch: at least one round, strictly consecutive, all effects of a round carrying
    /// that round.
    /// </summary>
    /// <exception cref="ArgumentException">The batch is empty, has a gap or a repeated round, or an effect of another round.</exception>
    public static void EnsureValid(IReadOnlyList<RoundWrite> rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        if (rounds.Count == 0)
        {
            throw new ArgumentException("A batch must contain at least one round.", nameof(rounds));
        }

        for (var i = 0; i < rounds.Count; i++)
        {
            var round = rounds[i].Status.IndexedRound;
            if (i > 0 && round != rounds[i - 1].Status.IndexedRound + 1)
            {
                throw new ArgumentException($"Round {round} does not follow round {rounds[i - 1].Status.IndexedRound} in the batch.", nameof(rounds));
            }

            if (rounds[i].Effects.Any(e => e.Id.Round != round))
            {
                throw new ArgumentException($"All effects must belong to round {round}.", nameof(rounds));
            }
        }
    }
}
