namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>One writer term: from <see cref="FromRound"/> on, this build of the indexer wrote the database (until the next term).</summary>
/// <param name="FromRound">The first round the writer was about to write.</param>
/// <param name="Version">The build version of the writer.</param>
/// <param name="StartedAt">When the term began.</param>
public sealed record IndexingTerm(ulong FromRound, string Version, DateTimeOffset StartedAt);

/// <summary>Which build wrote which rounds (ADR 0021): one record per writer term, so "rounds written by an affected build" is a lookup.</summary>
public interface IIndexingLog
{
    /// <summary>
    /// Records a term. A term with the same round and version as the newest stored one is not stored again (the first time is kept), so a
    /// writer that restarts without indexing anything does not add a row per restart.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AppendAsync(IndexingTerm term, CancellationToken cancellationToken);

    /// <summary>The recorded terms, newest first (highest first round first).</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<IndexingTerm>> ListAsync(CancellationToken cancellationToken);
}
