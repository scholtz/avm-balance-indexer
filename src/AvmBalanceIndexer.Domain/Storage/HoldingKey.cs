using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Identifies one account/token balance.</summary>
public readonly record struct HoldingKey(Address Account, TokenKey Token);
