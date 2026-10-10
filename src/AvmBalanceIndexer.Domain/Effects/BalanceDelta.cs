using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>
/// A signed balance change derived from chain data, before the ordered indexer assigns a running balance.
/// </summary>
public sealed record BalanceDelta(
    EffectId Id,
    Address Account,
    TokenKey Token,
    EffectType Type,
    BigInteger Delta,
    Address? Counterparty);
