namespace AvmBalanceIndexer.Domain.Tokens;

/// <summary>Stable lower-case wire/storage names for <see cref="TokenKind"/>.</summary>
public static class TokenKindExtensions
{
    public static string ToStorageName(this TokenKind kind) => kind switch
    {
        TokenKind.Algo => "algo",
        TokenKind.Asa => "asa",
        TokenKind.Arc200 => "arc200",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown token kind."),
    };

    public static bool TryParseStorageName(string? name, out TokenKind kind)
    {
        switch (name)
        {
            case "algo":
                kind = TokenKind.Algo;
                return true;
            case "asa":
                kind = TokenKind.Asa;
                return true;
            case "arc200":
                kind = TokenKind.Arc200;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
