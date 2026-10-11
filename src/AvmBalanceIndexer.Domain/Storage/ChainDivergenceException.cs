namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Raised when the data presented for a round contradicts what is already published (hash mismatch) or would leave a gap.
/// The database must be rebuilt from the last verified round rather than patched.
/// </summary>
public sealed class ChainDivergenceException(string message) : Exception(message);
