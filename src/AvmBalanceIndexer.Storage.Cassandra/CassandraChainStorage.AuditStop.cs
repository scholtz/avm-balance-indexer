using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage
{
    public async Task RecordAsync(AuditStop auditStop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditStop);

        // Conditional, so the first record of a round is kept, and only ever conditional (see the indexing log).
        var insert = await BindAsync(
            $"INSERT INTO {Table("audit_stop")} (chain, round, reason, published_round, recorded_at, acknowledged) VALUES (?, ?, ?, ?, ?, false) IF NOT EXISTS",
            cancellationToken,
            _chain,
            unchecked((long)auditStop.Round),
            auditStop.Reason,
            unchecked((long)auditStop.PublishedRound),
            auditStop.RecordedAt).ConfigureAwait(false);
        await ExecuteConditionalAsync(insert, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuditStop?> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await ExecuteAsync($"SELECT round, reason, published_round, recorded_at, acknowledged FROM {Table("audit_stop")} WHERE chain = ?", cancellationToken, _chain).ConfigureAwait(false);
        return rows
            .Select(static row => new AuditStop(unchecked((ulong)row.GetValue<long>("round")), row.GetValue<string>("reason"), unchecked((ulong)row.GetValue<long>("published_round")), row.GetValue<DateTimeOffset>("recorded_at"), row.GetValue<bool>("acknowledged")))
            .Where(static auditStop => !auditStop.Acknowledged)
            .OrderBy(static auditStop => auditStop.Round)
            .FirstOrDefault();
    }

    public async Task<bool> AcknowledgeAsync(ulong round, CancellationToken cancellationToken)
    {
        var update = await BindAsync(
            $"UPDATE {Table("audit_stop")} SET acknowledged = true WHERE chain = ? AND round = ? IF EXISTS",
            cancellationToken,
            _chain,
            unchecked((long)round)).ConfigureAwait(false);
        return await ExecuteConditionalAsync(update, cancellationToken).ConfigureAwait(false);
    }
}
