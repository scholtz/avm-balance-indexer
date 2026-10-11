using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>
/// One immutable change to an account's balance of one token. <see cref="BalanceAfter"/> is the exact
/// end-of-effect balance computed by the ordered indexer, which makes "balance at round R" a latest-row lookup.
/// </summary>
public sealed record TokenEffect(
    EffectId Id,
    Address Account,
    TokenKey Token,
    EffectType Type,
    BigInteger Delta,
    Amount BalanceAfter,
    Address? Counterparty)
{
    /// <summary>Creates the stored effect from a delta and the account's balance after applying it.</summary>
    public static TokenEffect From(BalanceDelta delta, Amount balanceAfter)
    {
        ArgumentNullException.ThrowIfNull(delta);
        return new TokenEffect(delta.Id, delta.Account, delta.Token, delta.Type, delta.Delta, balanceAfter, delta.Counterparty);
    }
}
