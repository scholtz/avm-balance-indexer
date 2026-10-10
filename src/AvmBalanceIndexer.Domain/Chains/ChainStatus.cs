namespace AvmBalanceIndexer.Domain.Chains;

/// <summary>Publication cursor for one chain. The API must never expose data beyond <see cref="IndexedRound"/>.</summary>
public sealed record ChainStatus(
    string Chain,
    ulong IndexedRound,
    ReadOnlyMemory<byte> IndexedBlockHash,
    ulong? TargetRound,
    IngestionState State,
    DateTimeOffset UpdatedAt)
{
    public ulong Lag => TargetRound is { } target && target > IndexedRound ? target - IndexedRound : 0;
}
