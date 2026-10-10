namespace AvmBalanceIndexer.Domain.Tokens;

/// <summary>
/// Canonical token classification. ARC-20 Smart ASAs are indexed as <see cref="Asa"/> with
/// controlling-application metadata; they are never a separate balance stream.
/// </summary>
public enum TokenKind
{
    /// <summary>Native network token (ALGO / VOI). Token id is always 0.</summary>
    Algo = 0,

    /// <summary>Algorand Standard Asset, keyed by asset id. Includes ARC-20 Smart ASAs.</summary>
    Asa = 1,

    /// <summary>ARC-200 application token, keyed by application id.</summary>
    Arc200 = 2,
}
