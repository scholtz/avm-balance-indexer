using System.Globalization;
using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Npgsql;
using NpgsqlTypes;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage : IScanJobStore
{
    private const string JobColumns = "id, token_kind, token_id, round, include_zero, state, holders_found, cursor, error, created_at, updated_at, expires_at";

    public async Task CreateAsync(ScanJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO scan_job (id, token_kind, token_id, round, include_zero, state, created_at, updated_at, expires_at)
            VALUES (@id, @kind, @tokenId, @round, @zero, @state, @created, @updated, @expires)
            """);
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("kind", job.Token.Kind.ToStorageName());
        command.Parameters.AddWithValue("tokenId", StorageId(job.Token.TokenId));
        command.Parameters.AddWithValue("round", unchecked((long)job.Round));
        command.Parameters.AddWithValue("zero", job.IncludeZero);
        command.Parameters.AddWithValue("state", job.State.ToString());
        command.Parameters.AddWithValue("created", job.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated", job.UpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue("expires", job.ExpiresAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScanJob?> GetJobAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand($"SELECT {JobColumns} FROM scan_job WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadJob(reader) : null;
    }

    public async Task<int> CountActiveAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT count(*) FROM scan_job WHERE state IN ('Pending', 'Running') AND expires_at > @now");
        command.Parameters.AddWithValue("now", now.UtcDateTime);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<ScanJob?> ClaimNextAsync(string owner, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        // SKIP LOCKED lets several replicas claim in parallel without waiting for each other.
        await using var command = _dataSource.CreateCommand(
            $"""
            UPDATE scan_job SET state = 'Running', owner = @owner, updated_at = @now
            WHERE id = (
                SELECT id FROM scan_job
                WHERE expires_at > @now AND (state = 'Pending' OR (state = 'Running' AND updated_at < @stale))
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING {JobColumns}
            """);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("now", now.UtcDateTime);
        command.Parameters.AddWithValue("stale", staleBefore.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadJob(reader) : null;
    }

    public async Task<bool> AppendAsync(Guid id, string owner, IReadOnlyList<Holding> items, string? nextCursor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The row lock makes the check of ownership and the numbering of the new rows one step.
        long found;
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT holders_found FROM scan_job WHERE id = @id AND owner = @owner AND state = 'Running' FOR UPDATE", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("id", id);
            lockCommand.Parameters.AddWithValue("owner", owner);
            if (await lockCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not long current)
            {
                return false;
            }

            found = current;
        }

        if (items.Count > 0)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO scan_result (job_id, seq, account, amount, opted_in, last_changed_round)
                SELECT @id, @first + u.n - 1, u.account, u.amount::numeric, u.opted_in, u.round
                FROM unnest(@account, @amount, @optedin, @round) WITH ORDINALITY AS u(account, amount, opted_in, round, n)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("first", found);
            AddArray(insert, "account", NpgsqlDbType.Bytea, items.Select(static h => h.Account.ToArray()).ToArray());
            AddArray(insert, "amount", NpgsqlDbType.Text, items.Select(static h => h.Amount.ToString()).ToArray());
            AddArray(insert, "optedin", NpgsqlDbType.Boolean, items.Select(static h => h.OptedIn).ToArray());
            AddArray(insert, "round", NpgsqlDbType.Bigint, items.Select(static h => unchecked((long)h.LastChangedRound)).ToArray());
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE scan_job SET holders_found = @found, cursor = @cursor, updated_at = @now WHERE id = @id",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("found", found + items.Count);
            update.Parameters.Add(new NpgsqlParameter("cursor", NpgsqlDbType.Text) { Value = (object?)nextCursor ?? DBNull.Value });
            update.Parameters.AddWithValue("now", now.UtcDateTime);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<bool> CompleteAsync(Guid id, string owner, DateTimeOffset now, CancellationToken cancellationToken) =>
        FinishAsync(id, owner, ScanState.Completed, null, now, cancellationToken);

    public Task<bool> FailAsync(Guid id, string owner, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        FinishAsync(id, owner, ScanState.Failed, reason, now, cancellationToken);

    public async Task<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            "UPDATE scan_job SET state = 'Cancelled', updated_at = @now WHERE id = @id AND state IN ('Pending', 'Running')");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("now", now.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<Page<Holding>> GetResultAsync(Guid id, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        long after = -1;
        if (cursor is not null && !long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out after))
        {
            throw new ArgumentException("Invalid cursor.", nameof(cursor));
        }

        if (await GetJobAsync(id, cancellationToken).ConfigureAwait(false) is not { } job)
        {
            return new Page<Holding>([], null, 0);
        }

        await using var command = _dataSource.CreateCommand(
            """
            SELECT seq, account, amount, opted_in, last_changed_round
            FROM scan_result
            WHERE job_id = @id AND seq > @after
            ORDER BY seq
            LIMIT @limit
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("after", after);
        command.Parameters.AddWithValue("limit", pageSize + 1);

        var items = new List<Holding>();
        var seqs = new List<long>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new Holding(
                    new Address((byte[])reader[1]),
                    job.Token,
                    ToAmount(reader.GetFieldValue<BigInteger>(2)),
                    reader.GetBoolean(3),
                    (ulong)reader.GetInt64(4)));
                seqs.Add(reader.GetInt64(0));
            }
        }

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        // A reader that follows a running scan needs a position even when it has caught up.
        var delivered = items.Count > 0 ? seqs[items.Count - 1] : after;
        var next = delivered >= 0 && (hasMore || !job.IsFinished) ? delivered.ToString(CultureInfo.InvariantCulture) : null;
        return new Page<Holding>(items, next, job.Round);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var results = new NpgsqlCommand(
            "DELETE FROM scan_result WHERE job_id IN (SELECT id FROM scan_job WHERE expires_at <= @now)", connection, transaction))
        {
            results.Parameters.AddWithValue("now", now.UtcDateTime);
            await results.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        int removed;
        await using (var jobs = new NpgsqlCommand("DELETE FROM scan_job WHERE expires_at <= @now", connection, transaction))
        {
            jobs.Parameters.AddWithValue("now", now.UtcDateTime);
            removed = await jobs.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return removed;
    }

    private async Task<bool> FinishAsync(Guid id, string owner, ScanState state, string? error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            "UPDATE scan_job SET state = @state, error = @error, updated_at = @now WHERE id = @id AND owner = @owner AND state = 'Running'");
        command.Parameters.AddWithValue("state", state.ToString());
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value });
        command.Parameters.AddWithValue("now", now.UtcDateTime);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("owner", owner);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static TokenKind ParseKind(string stored) =>
        TokenKindExtensions.TryParseStorageName(stored, out var kind) ? kind : throw new InvalidDataException($"The database holds an unknown token kind \"{stored}\".");

    private static ScanJob ReadJob(NpgsqlDataReader reader)
    {
        return new ScanJob(
            reader.GetGuid(0),
            new TokenKey(ParseKind(reader.GetString(1)), (ulong)reader.GetInt64(2)),
            (ulong)reader.GetInt64(3),
            reader.GetBoolean(4),
            Enum.Parse<ScanState>(reader.GetString(5)),
            reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero),
            new DateTimeOffset(reader.GetDateTime(10), TimeSpan.Zero),
            new DateTimeOffset(reader.GetDateTime(11), TimeSpan.Zero));
    }
}
