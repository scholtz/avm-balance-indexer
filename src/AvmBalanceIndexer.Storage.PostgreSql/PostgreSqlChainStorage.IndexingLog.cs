using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage
{
    public async Task AppendAsync(IndexingTerm term, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(term);
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO indexing_term (from_round, version, started_at) VALUES (@round, @version, @at) ON CONFLICT DO NOTHING");
        command.Parameters.AddWithValue("round", unchecked((long)term.FromRound));
        command.Parameters.AddWithValue("version", term.Version);
        command.Parameters.AddWithValue("at", term.StartedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndexingTerm>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("SELECT from_round, version, started_at FROM indexing_term ORDER BY from_round DESC, started_at DESC, version");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var terms = new List<IndexingTerm>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            terms.Add(new IndexingTerm(
                unchecked((ulong)reader.GetInt64(0)),
                reader.GetString(1),
                new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero)));
        }

        return terms;
    }
}
