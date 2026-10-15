using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>What a block does to balances and to the set of known tokens.</summary>
/// <param name="Deltas">
/// Balance deltas in ledger execution order. The two halves of an asset close-out that the block does not quantify
/// (<see cref="CloseOutRole"/>) carry a zero placeholder amount; <c>BalanceTracker</c> resolves it from the running balance, so
/// code that sums deltas must treat them specially.
/// </param>
/// <param name="TokenChanges">Token records created or destroyed by the block (full replacement records, in block order).</param>
public sealed record NormalizedBlock(IReadOnlyList<BalanceDelta> Deltas, IReadOnlyList<TokenInfo> TokenChanges);
