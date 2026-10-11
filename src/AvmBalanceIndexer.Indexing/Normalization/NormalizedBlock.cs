using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>What a block does to balances and to the set of known tokens.</summary>
/// <param name="Deltas">Balance deltas in ledger execution order.</param>
/// <param name="TokenChanges">Token records created or destroyed by the block (full replacement records, in block order).</param>
public sealed record NormalizedBlock(IReadOnlyList<BalanceDelta> Deltas, IReadOnlyList<TokenInfo> TokenChanges);
