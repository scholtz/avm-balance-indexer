namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>
/// Deterministic identity and total order of a balance effect within a chain.
/// Re-deriving the same round must yield identical ids so that replay is idempotent.
/// </summary>
/// <param name="Round">Round in which the effect happened (0 for genesis allocations).</param>
/// <param name="IntraRoundOrder">
/// 0 for effects at the start of the block, the zero-based transaction index plus one for transaction effects, and the
/// transaction count plus one for effects at the end of the block.
/// </param>
/// <param name="TransactionId">Top-level transaction id (empty for block-level and genesis effects).</param>
/// <param name="InnerPath">Position of the inner transaction within its parent tree; empty for top-level transactions.</param>
/// <param name="Ordinal">Zero-based sequence number of the effect within the round, in ledger execution order.</param>
public sealed record EffectId(
    ulong Round,
    int IntraRoundOrder,
    string TransactionId,
    IReadOnlyList<int> InnerPath,
    int Ordinal)
{
    /// <summary>Value equality including <see cref="InnerPath"/> contents.</summary>
    public bool Equals(EffectId? other) =>
        other is not null
        && Round == other.Round
        && IntraRoundOrder == other.IntraRoundOrder
        && Ordinal == other.Ordinal
        && string.Equals(TransactionId, other.TransactionId, StringComparison.Ordinal)
        && InnerPath.SequenceEqual(other.InnerPath);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Round);
        hash.Add(IntraRoundOrder);
        hash.Add(Ordinal);
        hash.Add(TransactionId, StringComparer.Ordinal);
        foreach (var step in InnerPath)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }
}
