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
    Address? Counterparty);
