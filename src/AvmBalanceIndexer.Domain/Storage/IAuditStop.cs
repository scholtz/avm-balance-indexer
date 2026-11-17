namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>A round at which the ledger audit found that the database disagrees with the ledger (ADR 0021), and that stopped the writer.</summary>
/// <param name="Round">The first round that disagreed.</param>
/// <param name="Reason">What disagreed (account, token, indexed and ledger amount).</param>
/// <param name="PublishedRound">The round this writer had published when it stopped (the audit runs after publication, so it is at or after <see cref="Round"/>).</param>
/// <param name="RecordedAt">When the auditStop was recorded.</param>
/// <param name="Acknowledged">True once an operator decided that the round is fine (for example a difference on the side of the oracle).</param>
public sealed record AuditStop(ulong Round, string Reason, ulong PublishedRound, DateTimeOffset RecordedAt, bool Acknowledged);

/// <summary>
/// The auditStop of the ledger audit, kept in the database so that it survives a restart: a recorded, unacknowledged auditStop keeps every later
/// writer from indexing past the bad round, until an operator acknowledges it or the database is rebuilt from an earlier round.
/// </summary>
public interface IAuditStop
{
    /// <summary>Records a auditStop. A round that has a record keeps the first one (a restart that audits the same round again changes nothing).</summary>
    /// <param name="auditStop">The auditStop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RecordAuditStopAsync(AuditStop auditStop, CancellationToken cancellationToken);

    /// <summary>The unacknowledged auditStop with the smallest round, or null when there is none.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<AuditStop?> GetAuditStopAsync(CancellationToken cancellationToken);

    /// <summary>Marks the auditStop of a round as acknowledged.</summary>
    /// <param name="round">The round of the auditStop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>False when no auditStop is recorded for that round.</returns>
    Task<bool> AcknowledgeAuditStopAsync(ulong round, CancellationToken cancellationToken);
}
