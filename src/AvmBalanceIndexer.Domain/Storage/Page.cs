namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Cursor-based page. Cursors are opaque to callers and stable across providers.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, ulong IndexedRound);
