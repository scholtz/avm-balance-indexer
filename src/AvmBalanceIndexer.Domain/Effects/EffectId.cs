namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>
/// Deterministic identity of a balance effect: (round, intraRoundOrder, transactionId, innerPath, ordinal).
/// Re-deriving the same round must yield identical ids so that replay is idempotent.
/// </summary>
public sealed record EffectId(
    ulong Round,
    int IntraRoundOrder,
    string TransactionId,
    IReadOnlyList<int> InnerPath,
    int Ordinal);
