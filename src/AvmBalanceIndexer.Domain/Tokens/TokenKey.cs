namespace AvmBalanceIndexer.Domain.Tokens;

/// <summary>Chain-scoped canonical identity of a token: (kind, tokenId).</summary>
public readonly record struct TokenKey(TokenKind Kind, ulong TokenId)
{
    /// <summary>The native token of the chain.</summary>
    public static TokenKey Native { get; } = new(TokenKind.Algo, 0);

    public static TokenKey Asa(ulong assetId) => new(TokenKind.Asa, assetId);

    public static TokenKey Arc200(ulong applicationId) => new(TokenKind.Arc200, applicationId);

    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}:{TokenId}";
}
