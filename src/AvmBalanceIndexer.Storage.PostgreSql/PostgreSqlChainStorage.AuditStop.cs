using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage
{
    public async Task RecordAuditStopAsync(AuditStop auditStop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditStop);
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO audit_stop (round, reason, published_round, recorded_at, acknowledged) VALUES (@round, @reason, @published, @at, false) ON CONFLICT DO NOTHING");
        command.Parameters.AddWithValue("round", unchecked((long)auditStop.Round));
        command.Parameters.AddWithValue("reason", auditStop.Reason);
        command.Parameters.AddWithValue("published", unchecked((long)auditStop.PublishedRound));
        command.Parameters.AddWithValue("at", auditStop.RecordedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuditStop?> GetAuditStopAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT round, reason, published_round, recorded_at, acknowledged FROM audit_stop WHERE NOT acknowledged ORDER BY round LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new AuditStop(unchecked((ulong)reader.GetInt64(0)), reader.GetString(1), unchecked((ulong)reader.GetInt64(2)), new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero), reader.GetBoolean(4))
            : null;
    }

    public async Task<bool> AcknowledgeAuditStopAsync(ulong round, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("UPDATE audit_stop SET acknowledged = true WHERE round = @round AND NOT acknowledged");
        command.Parameters.AddWithValue("round", unchecked((long)round));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }
}
