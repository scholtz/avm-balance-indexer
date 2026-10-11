namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Exclusive right to be the one ordered writer of a chain. Held by exactly one worker replica at a time; the others wait
/// as standbys. Disposing the lease releases it.
/// </summary>
public interface IWriterLease : IAsyncDisposable
{
    /// <summary>
    /// Cancelled when the lease can no longer be trusted (its session or renewal failed, or another owner took it). The holder
    /// must stop writing at once. This is a best-effort signal: round publication is independently protected by the cursor
    /// compare-and-set (Cassandra) or the writer lock inside the commit transaction (PostgreSQL).
    /// </summary>
    CancellationToken Lost { get; }
}

/// <summary>Hands out the writer lease of the chain a storage serves.</summary>
public interface IWriterLeaseProvider
{
    /// <summary>Takes the lease when it is free; returns null without waiting when another owner holds it.</summary>
    Task<IWriterLease?> TryAcquireAsync(CancellationToken cancellationToken);
}
