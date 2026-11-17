using AvmBalanceIndexer.Domain.Storage;
using Npgsql;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage
{
    private const int RewindSteps = 8;

    /// <summary>
    /// Rewinds the database to <c>Request.Round</c> in one transaction (ADR 0021): readers see the old state until the commit, then the
    /// rewound one. The advisory lock of the round writer is taken (so a writer that is still running waits, and then fails against the new
    /// cursor), and the request id is recorded in the same transaction, so the request runs once. The schema must exist (the worker applies
    /// it before it asks for the lease, and the rewind runs inside a writer term).
    /// </summary>
    public async Task<PartialRebuildResult> RunAsync(ReadOnlyMemory<byte> blockHashAtRound, IProgress<PartialRebuildProgress>? progress, CancellationToken cancellationToken)
    {
        var request = Request ?? throw new InvalidOperationException("No rewind is configured (Storage:PostgreSql:RewindTo).");
        if (blockHashAtRound.Length != 32)
        {
            throw new ArgumentException("The hash of the block at the rewind round (32 bytes) is required.", nameof(blockHashAtRound));
        }

        var round = unchecked((long)request.Round);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // A rewind of a big database runs for minutes; the read timeout of the connection is not meant for it.
        await RewindAsync(connection, transaction, "SET LOCAL statement_timeout = 0", [], cancellationToken).ConfigureAwait(false);
        await LockWriterAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        if (await RewindScalarAsync(connection, transaction, "SELECT 1 FROM maintenance_request WHERE request_id = @id", [("id", request.Source)], cancellationToken).ConfigureAwait(false) is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new PartialRebuildResult(PartialRebuildOutcome.AlreadyDone, $"The request {request.Source} was carried out before.");
        }

        var cursor = await LockCursorAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (cursor is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new PartialRebuildResult(PartialRebuildOutcome.Refused, "Nothing is indexed in this database; there is nothing to rewind.");
        }

        if (cursor.Value.Round < request.Round)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new PartialRebuildResult(PartialRebuildOutcome.Refused, $"The database is at round {cursor.Value.Round}, before the requested round {request.Round}.");
        }

        // At the round itself nothing is deleted, but the hash is the cursor's link to the next block: a different one is a different chain.
        if (cursor.Value.Round == request.Round && !cursor.Value.Hash.AsSpan().SequenceEqual(blockHashAtRound.Span))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new PartialRebuildResult(PartialRebuildOutcome.Refused, $"The database is at round {request.Round} with another block hash than the one given.");
        }

        long deleted = 0;
        long holdings = 0;
        if (cursor.Value.Round > request.Round)
        {
            (deleted, holdings) = await DeleteAfterRoundAsync(connection, transaction, request.Round, progress, cancellationToken).ConfigureAwait(false);
        }

        if (cursor.Value.Round > request.Round)
        {
            await RewindAsync(
                connection,
                transaction,
                "UPDATE chain_status SET indexed_round = @r, indexed_block_hash = @hash, target_round = NULL, status = 'Backfilling', updated_at = now() WHERE chain = @chain",
                [("r", round), ("hash", blockHashAtRound.ToArray()), ("chain", _chain)],
                cancellationToken).ConfigureAwait(false);
        }
        await RewindAsync(
            connection,
            transaction,
            "INSERT INTO maintenance_request (request_id, kind, round, executed_at) VALUES (@id, 'rewind', @r, now())",
            [("id", request.Source), ("r", round)],
            cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartialRebuildProgress("cursor", 0, RewindSteps - 1, RewindSteps));

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartialRebuildProgress("committed", deleted, RewindSteps, RewindSteps));
        return new PartialRebuildResult(PartialRebuildOutcome.Done, string.Empty, deleted, holdings);
    }

    private static async Task<(long Deleted, long Holdings)> DeleteAfterRoundAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ulong keepThrough,
        IProgress<PartialRebuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var parameters = new (string, object)[] { ("r", unchecked((long)keepThrough)), ("b", RoundBuckets.ForRound(keepThrough)) };

        // The pairs that lose effects, found through the token table (range-partitioned by bucket: older partitions are pruned).
        await RewindAsync(
            connection,
            transaction,
            "CREATE TEMP TABLE rewind_affected ON COMMIT DROP AS SELECT DISTINCT account, token_kind, token_id FROM effects_by_token_bucket WHERE bucket >= @b AND round > @r",
            parameters,
            cancellationToken).ConfigureAwait(false);
        await RewindAsync(connection, transaction, "CREATE UNIQUE INDEX ON rewind_affected (account, token_kind, token_id)", [], cancellationToken).ConfigureAwait(false);

        // Temporary tables are never analyzed by autovacuum; without statistics the planner would treat them as tiny.
        await RewindAsync(connection, transaction, "ANALYZE rewind_affected", [], cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartialRebuildProgress("affected pairs", 0, 1, RewindSteps));

        var deleted = await RewindAsync(
            connection,
            transaction,
            """
            DELETE FROM effects_by_account_token e USING rewind_affected a
            WHERE e.account = a.account AND e.token_kind = a.token_kind AND e.token_id = a.token_id AND e.round > @r
            """,
            parameters,
            cancellationToken).ConfigureAwait(false);
        await RewindAsync(connection, transaction, "DELETE FROM effects_by_token_bucket WHERE bucket >= @b AND round > @r", parameters, cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartialRebuildProgress("effects", deleted, 2, RewindSteps));

        // The holding of a pair is its newest remaining effect; a pair without one loses both holding rows. The opted-in rule is the round
        // writer's: everything but an opt-out keeps the opt-in.
        await RewindAsync(
            connection,
            transaction,
            """
            CREATE TEMP TABLE rewind_newest ON COMMIT DROP AS
            SELECT DISTINCT ON (e.account, e.token_kind, e.token_id)
                   e.account, e.token_kind, e.token_id, e.balance_after AS amount, (e.effect_type <> 'AssetOptOut') AS opted_in, e.round
            FROM effects_by_account_token e
            JOIN rewind_affected a ON e.account = a.account AND e.token_kind = a.token_kind AND e.token_id = a.token_id
            ORDER BY e.account, e.token_kind, e.token_id, e.round DESC, e.intra_round_order DESC, e.ordinal DESC
            """,
            [],
            cancellationToken).ConfigureAwait(false);
        await RewindAsync(connection, transaction, "CREATE UNIQUE INDEX ON rewind_newest (account, token_kind, token_id)", [], cancellationToken).ConfigureAwait(false);
        await RewindAsync(connection, transaction, "ANALYZE rewind_newest", [], cancellationToken).ConfigureAwait(false);

        long holdings = 0;
        foreach (var table in (string[])["holdings_by_account", "holdings_by_token"])
        {
            await RewindAsync(
                connection,
                transaction,
                $"""
                DELETE FROM {table} h USING rewind_affected a
                WHERE h.account = a.account AND h.token_kind = a.token_kind AND h.token_id = a.token_id
                  AND NOT EXISTS (SELECT 1 FROM rewind_newest n WHERE n.account = a.account AND n.token_kind = a.token_kind AND n.token_id = a.token_id)
                """,
                [],
                cancellationToken).ConfigureAwait(false);
            var written = await RewindAsync(
                connection,
                transaction,
                $"""
                INSERT INTO {table} (account, token_kind, token_id, amount, opted_in, last_changed_round)
                SELECT account, token_kind, token_id, amount, opted_in, round FROM rewind_newest
                ON CONFLICT (account, token_kind, token_id) DO UPDATE
                  SET amount = EXCLUDED.amount, opted_in = EXCLUDED.opted_in, last_changed_round = EXCLUDED.last_changed_round
                """,
                [],
                cancellationToken).ConfigureAwait(false);
            holdings = Math.Max(holdings, written);
        }

        progress?.Report(new PartialRebuildProgress("holdings", holdings, 3, RewindSteps));

        // The touched directory: pairs first seen after the round are gone, the others lose the part of their range after it.
        await RewindAsync(
            connection,
            transaction,
            """
            DELETE FROM accounts_touched_by_token t USING rewind_affected a
            WHERE t.account = a.account AND t.token_kind = a.token_kind AND t.token_id = a.token_id AND t.first_round > @r
            """,
            parameters,
            cancellationToken).ConfigureAwait(false);
        await RewindAsync(
            connection,
            transaction,
            """
            UPDATE accounts_touched_by_token t SET last_round = n.round
            FROM rewind_newest n
            WHERE t.account = n.account AND t.token_kind = n.token_kind AND t.token_id = n.token_id AND t.last_round > @r
            """,
            parameters,
            cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartialRebuildProgress("touched accounts", 0, 4, RewindSteps));

        // Tokens: created after the round are gone, destroyed after it are alive again (other fields stay, ADR 0021).
        await RewindAsync(connection, transaction, "DELETE FROM token WHERE created_round > @r", parameters, cancellationToken).ConfigureAwait(false);
        await RewindAsync(connection, transaction, "UPDATE token SET deleted_round = NULL WHERE deleted_round > @r", parameters, cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartialRebuildProgress("tokens", 0, 5, RewindSteps));

        // Scans of rounds that are no longer indexed (their results go with them), and the build records after the round.
        await RewindAsync(connection, transaction, "DELETE FROM scan_result WHERE job_id IN (SELECT id FROM scan_job WHERE round > @r)", parameters, cancellationToken).ConfigureAwait(false);
        await RewindAsync(connection, transaction, "DELETE FROM scan_job WHERE round > @r", parameters, cancellationToken).ConfigureAwait(false);
        await RewindAsync(connection, transaction, "DELETE FROM indexing_term WHERE from_round > @r", parameters, cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartialRebuildProgress("scans and build records", 0, 6, RewindSteps));
        return (deleted, holdings);
    }

    private static async Task<int> RewindAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, (string Name, object Value)[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> RewindScalarAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, (string Name, object Value)[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }
}
