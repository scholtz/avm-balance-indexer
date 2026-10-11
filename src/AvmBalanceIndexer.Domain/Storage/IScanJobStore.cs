using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Storage;

public enum ScanState
{
    /// <summary>Accepted, waiting for a worker replica to pick it up.</summary>
    Pending = 0,

    /// <summary>A worker replica is reading the holders and appending them to the result.</summary>
    Running = 1,

    /// <summary>All holders are in the result.</summary>
    Completed = 2,

    /// <summary>The scan stopped with an error that retrying will not fix.</summary>
    Failed = 3,

    /// <summary>The requester cancelled it.</summary>
    Cancelled = 4,
}

/// <summary>
/// One asynchronous holders-at-round scan: a consistent export of every holder of a token at a past round, produced page by page
/// by a worker and read back by the requester.
/// </summary>
/// <param name="Id">Identity handed to the requester.</param>
/// <param name="Token">Token whose holders are scanned.</param>
/// <param name="Round">Round the holders are read at (published when the scan was accepted).</param>
/// <param name="IncludeZero">Whether zero balances are part of the result.</param>
/// <param name="State">Lifecycle state.</param>
/// <param name="HoldersFound">Rows already in the result.</param>
/// <param name="Cursor">Where the executor resumes after a crash (a <see cref="IBalanceReadStore.GetHoldersAtRoundAsync"/> cursor).</param>
/// <param name="Error">Why a failed scan failed.</param>
/// <param name="CreatedAt">When the scan was accepted.</param>
/// <param name="UpdatedAt">Last state change or checkpoint; a running scan whose update is old is considered abandoned.</param>
/// <param name="ExpiresAt">After this the scan and its result are deleted.</param>
public sealed record ScanJob(
    Guid Id,
    TokenKey Token,
    ulong Round,
    bool IncludeZero,
    ScanState State,
    long HoldersFound,
    string? Cursor,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt)
{
    /// <summary>Stored as the cursor once the last page of holders was appended, so a resumed scan completes instead of starting over.</summary>
    public const string EndOfList = "end";

    public bool IsFinished => State is ScanState.Completed or ScanState.Failed or ScanState.Cancelled;

    /// <summary>All holders are stored; only the completion is missing.</summary>
    public bool IsFullyRead => Cursor == EndOfList;
}

/// <summary>
/// Durable queue and result store of asynchronous scans. Several worker replicas may claim jobs at the same time; a job belongs to
/// exactly one owner at a time, and an owner that stops checkpointing loses it after the stale timeout, so another replica resumes it
/// from the stored cursor. All mutations by an owner are conditional on still being the owner of a running job.
/// </summary>
public interface IScanJobStore
{
    Task CreateAsync(ScanJob job, CancellationToken cancellationToken);

    Task<ScanJob?> GetJobAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Jobs that are accepted or running (they count against the admission limit).</summary>
    Task<int> CountActiveAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the oldest pending job, or a running job whose owner has not checkpointed since <paramref name="staleBefore"/>, and
    /// makes <paramref name="owner"/> its owner. Returns null when there is nothing to do.
    /// </summary>
    Task<ScanJob?> ClaimNextAsync(string owner, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken cancellationToken);

    /// <summary>
    /// Appends rows (numbered continuing after <see cref="ScanJob.HoldersFound"/>) and moves the resume cursor, in one step.
    /// Returns false, changing nothing, when the caller no longer owns a running job (cancelled, expired or taken over).
    /// </summary>
    Task<bool> AppendAsync(Guid id, string owner, IReadOnlyList<Holding> items, string? nextCursor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> CompleteAsync(Guid id, string owner, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> FailAsync(Guid id, string owner, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Cancels a pending or running job. Returns false when the job is unknown or already finished.</summary>
    Task<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// The result rows in order. The cursor is opaque; <see cref="Page{T}.IndexedRound"/> carries the scanned round. The returned
    /// cursor continues after the last row delivered whenever more rows exist <em>or may still arrive</em> (the job is not finished), so
    /// a reader can follow a running scan; it is null once a finished scan has no more rows.
    /// </summary>
    Task<Page<Holding>> GetResultAsync(Guid id, string? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>Removes expired jobs with their results; returns how many jobs were removed.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
