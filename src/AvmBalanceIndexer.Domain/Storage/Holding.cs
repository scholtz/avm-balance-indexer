using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Current holding row. <see cref="OptedIn"/> with a zero <see cref="Amount"/> is a valid state.</summary>
public sealed record Holding(
    Address Account,
    TokenKey Token,
    Amount Amount,
    bool OptedIn,
    ulong LastChangedRound);
