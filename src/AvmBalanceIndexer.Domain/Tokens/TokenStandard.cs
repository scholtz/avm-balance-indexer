namespace AvmBalanceIndexer.Domain.Tokens;

/// <summary>Detected token standard, kept separately from <see cref="TokenKind"/> because detection may be unverified.</summary>
public enum TokenStandard
{
    Unknown = 0,
    Asa = 1,
    Arc20 = 2,
    Arc200 = 3,
}
