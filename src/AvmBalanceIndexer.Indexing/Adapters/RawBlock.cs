namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Canonical, backend-neutral block payload kept for replay and benchmarking.</summary>
public sealed record RawBlock(ulong Round, ReadOnlyMemory<byte> BlockHash, ReadOnlyMemory<byte> Payload);
