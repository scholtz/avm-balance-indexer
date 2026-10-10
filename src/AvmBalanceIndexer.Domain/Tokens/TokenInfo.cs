using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Domain.Tokens;

/// <summary>Token metadata as stored in the provider's token table.</summary>
public sealed record TokenInfo(
    TokenKey Key,
    Address? Creator,
    int? Decimals,
    string? Symbol,
    string? Name,
    TokenStandard Standard,
    bool StandardVerified,
    ulong? ControllingAppId,
    ulong? CreatedRound,
    ulong? DeletedRound,
    string? MetadataJson);
