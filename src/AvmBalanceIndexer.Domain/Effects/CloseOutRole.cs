namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>
/// Marks the two halves of an asset close-out whose amount the block does not state. Early protocols do not record the closed
/// amount (no <c>aca</c> in the apply data), so the amount is the closer's whole remaining balance, which only the ordered
/// indexer knows. The debit is immediately followed by its credit.
/// </summary>
public enum CloseOutRole
{
    /// <summary>An ordinary delta whose amount is exact.</summary>
    None = 0,

    /// <summary>Removes the closer's whole remaining balance (the delta's own amount is a placeholder).</summary>
    Debit = 1,

    /// <summary>Gives the destination exactly what the preceding <see cref="Debit"/> removed (the delta's own amount is a placeholder).</summary>
    Credit = 2,
}
