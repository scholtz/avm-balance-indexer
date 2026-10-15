using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>
/// A signed balance change derived from chain data, before the ordered indexer assigns a running balance.
/// </summary>
/// <param name="Id">Deterministic identity and order of the effect.</param>
/// <param name="Account">The account whose balance changes.</param>
/// <param name="Token">The token that changes.</param>
/// <param name="Type">What caused the change.</param>
/// <param name="Delta">Signed change; a placeholder for the halves of a close-out (see <paramref name="CloseOut"/>).</param>
/// <param name="Counterparty">The other side of a transfer, when there is one.</param>
/// <param name="CloseOut">
/// <see cref="CloseOutRole.None"/> for every delta with an exact amount; otherwise the delta is half of a close-out whose
/// amount the ordered indexer resolves from the running balance.
/// </param>
public sealed record BalanceDelta(
    EffectId Id,
    Address Account,
    TokenKey Token,
    EffectType Type,
    BigInteger Delta,
    Address? Counterparty,
    CloseOutRole CloseOut = CloseOutRole.None);
