namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Takes over the data of an earlier database up to a round, so indexing continues after it (ADR 0021).</summary>
public interface IPartialRebuild
{
    /// <summary>The configured request, or null when none is configured.</summary>
    PartialRebuildRequest? Request { get; }

    /// <summary>
    /// True when this request was carried out before (the stored request matches), so the caller can skip the rebuild without fetching anything:
    /// a stale request left in the configuration must not depend on nodes that can no longer serve an old block.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<bool> IsCarriedOutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The round and block hash the source database stands at, so that the caller can check the source against the chain before the copy
    /// (blocks are hash-linked: the block of that round must have that hash). Null when there is nothing to check: the rebuild is in place,
    /// the request was carried out already, or the source cannot be read (then <see cref="RunAsync"/> says why).
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<SourceCursor?> ReadSourceCursorAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Carries the request out. Idempotent: when this database was already handled it does nothing and reports
    /// <see cref="PartialRebuildOutcome.AlreadyDone"/>.
    /// </summary>
    /// <param name="blockHashAtRound">The hash of block <c>Request.Round</c>, fetched by the caller.</param>
    /// <param name="progress">Receives progress reports; may be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PartialRebuildResult> RunAsync(ReadOnlyMemory<byte> blockHashAtRound, IProgress<PartialRebuildProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>The published round of the source of a partial rebuild and the hash of that block.</summary>
/// <param name="Round">The published round.</param>
/// <param name="Hash">The block hash stored with it.</param>
public sealed record SourceCursor(ulong Round, ReadOnlyMemory<byte> Hash);

/// <summary>A configured partial rebuild: where the data comes from (a source keyspace, or the id of an in-place request) and up to which round.</summary>
/// <param name="Source">The source keyspace (Cassandra) or the request id (PostgreSQL).</param>
/// <param name="Round">The last round that is kept.</param>
public sealed record PartialRebuildRequest(string Source, ulong Round);

/// <summary>How a partial rebuild ended.</summary>
public enum PartialRebuildOutcome
{
    /// <summary>The data was taken over or rewound.</summary>
    Done,

    /// <summary>The request was carried out before; nothing was changed.</summary>
    AlreadyDone,

    /// <summary>A precondition failed; nothing was changed. See the reason.</summary>
    Refused,
}

/// <summary>The result of a partial rebuild.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Reason">Why it was refused; empty otherwise.</param>
/// <param name="EffectsCopied">Effect rows copied (Cassandra) or deleted (PostgreSQL).</param>
/// <param name="HoldingsWritten">Holding rows written.</param>
public sealed record PartialRebuildResult(PartialRebuildOutcome Outcome, string Reason = "", long EffectsCopied = 0, long HoldingsWritten = 0);

/// <summary>A progress report of a running partial rebuild.</summary>
/// <param name="Stage">The step that was just finished.</param>
/// <param name="Rows">Rows handled by that step.</param>
/// <param name="RangesDone">Ranges finished (token ranges for Cassandra, steps for PostgreSQL).</param>
/// <param name="RangesTotal">Ranges in total.</param>
public sealed record PartialRebuildProgress(string Stage, long Rows, int RangesDone, int RangesTotal);
