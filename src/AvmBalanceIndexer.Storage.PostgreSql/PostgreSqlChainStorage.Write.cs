using System.Data;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Npgsql;
using NpgsqlTypes;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage
{
    /// <summary>Effects per statement; keeps array parameters and statement memory bounded for very large rounds such as genesis.</summary>
    private const int WriteBatchSize = 5_000;

    public async Task<bool> WriteRoundAsync(ChainStatus roundStatus, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roundStatus);
        ArgumentNullException.ThrowIfNull(effects);
        EnsureChain(roundStatus.Chain);

        var round = roundStatus.IndexedRound;
        if (effects.Any(e => e.Id.Round != round))
        {
            throw new ArgumentException($"All effects must belong to round {round}.", nameof(effects));
        }

        await EnsureBucketPartitionAsync(RoundBuckets.ForRound(round), cancellationToken).ConfigureAwait(false);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        await LockWriterAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var published = await LockCursorAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (published is null)
        {
            if (round != 0)
            {
                throw new ChainDivergenceException($"The first round written must be 0 but round {round} was given.");
            }
        }
        else if (round == published.Value.Round)
        {
            if (!roundStatus.IndexedBlockHash.Span.SequenceEqual(published.Value.Hash))
            {
                throw new ChainDivergenceException($"Round {round} is already published with a different block hash.");
            }

            // Idempotent replay of the latest published round: nothing to do.
            return false;
        }
        else if (round < published.Value.Round)
        {
            throw new ChainDivergenceException(
                $"Round {round} is older than the published round {published.Value.Round}; its block hash can no longer be verified, so it is refused.");
        }
        else if (round != published.Value.Round + 1)
        {
            throw new ChainDivergenceException($"Round {round} would leave a gap after published round {published.Value.Round}.");
        }

        if (effects.Count > 0)
        {
            foreach (var batch in effects.Chunk(WriteBatchSize))
            {
                await InsertAccountEffectsAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
                await InsertTokenEffectsAsync(connection, transaction, round, batch, cancellationToken).ConfigureAwait(false);
            }

            await UpsertHoldingsAsync(connection, transaction, round, effects, cancellationToken).ConfigureAwait(false);
            await UpsertTouchedAsync(connection, transaction, round, effects, cancellationToken).ConfigureAwait(false);
        }

        await UpsertCursorAsync(connection, transaction, roundStatus, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Serializes writers of this chain for the rest of the transaction. The cursor row lock alone cannot do that while no
    /// cursor row exists yet, so two workers starting on an empty database could both bootstrap.
    /// </summary>
    private async Task LockWriterAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@chain, 1))", connection, transaction);
        command.Parameters.AddWithValue("chain", _chain);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(ulong Round, byte[] Hash)?> LockCursorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT indexed_round, indexed_block_hash FROM chain_status WHERE chain = @chain FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("chain", _chain);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ((ulong)reader.GetInt64(0), (byte[])reader[1])
            : null;
    }

    private static async Task InsertAccountEffectsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TokenEffect[] effects, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO effects_by_account_token
              (account, token_kind, token_id, round, intra_round_order, ordinal, transaction_id, inner_path,
               effect_type, delta, balance_after, counterparty)
            SELECT u.account, u.token_kind, u.token_id, u.round, u.intra_round_order, u.ordinal, u.transaction_id,
                   CASE WHEN u.inner_path = '' THEN '{}'::integer[] ELSE string_to_array(u.inner_path, '.')::integer[] END,
                   u.effect_type, u.delta::numeric, u.balance_after::numeric, u.counterparty
            FROM unnest(@account, @kind, @id, @round, @order, @ordinal, @txid, @path, @type, @delta, @after, @counterparty)
                 AS u(account, token_kind, token_id, round, intra_round_order, ordinal, transaction_id, inner_path,
                      effect_type, delta, balance_after, counterparty)
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddArray(command, "account", NpgsqlDbType.Bytea, effects.Select(static e => e.Account.ToArray()).ToArray());
        AddArray(command, "kind", NpgsqlDbType.Text, effects.Select(static e => e.Token.Kind.ToStorageName()).ToArray());
        AddArray(command, "id", NpgsqlDbType.Bigint, effects.Select(static e => StorageId(e.Token.TokenId)).ToArray());
        AddArray(command, "round", NpgsqlDbType.Bigint, effects.Select(static e => unchecked((long)e.Id.Round)).ToArray());
        AddArray(command, "order", NpgsqlDbType.Integer, effects.Select(static e => e.Id.IntraRoundOrder).ToArray());
        AddArray(command, "ordinal", NpgsqlDbType.Integer, effects.Select(static e => e.Id.Ordinal).ToArray());
        AddArray(command, "txid", NpgsqlDbType.Text, effects.Select(static e => e.Id.TransactionId).ToArray());
        AddArray(command, "path", NpgsqlDbType.Text, effects.Select(static e => string.Join('.', e.Id.InnerPath)).ToArray());
        AddArray(command, "type", NpgsqlDbType.Text, effects.Select(static e => e.Type.ToString()).ToArray());
        AddArray(command, "delta", NpgsqlDbType.Text, effects.Select(static e => e.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        AddArray(command, "after", NpgsqlDbType.Text, effects.Select(static e => e.BalanceAfter.ToString()).ToArray());
        AddArray(command, "counterparty", NpgsqlDbType.Bytea, effects.Select(static e => e.Counterparty?.ToArray()).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTokenEffectsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ulong round, TokenEffect[] effects, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO effects_by_token_bucket
              (token_kind, token_id, bucket, round, intra_round_order, ordinal, account, transaction_id, effect_type, delta)
            SELECT u.token_kind, u.token_id, @bucket, @round, u.intra_round_order, u.ordinal, u.account,
                   u.transaction_id, u.effect_type, u.delta::numeric
            FROM unnest(@kind, @id, @order, @ordinal, @account, @txid, @type, @delta)
                 AS u(token_kind, token_id, intra_round_order, ordinal, account, transaction_id, effect_type, delta)
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("bucket", RoundBuckets.ForRound(round));
        command.Parameters.AddWithValue("round", unchecked((long)round));
        AddArray(command, "kind", NpgsqlDbType.Text, effects.Select(static e => e.Token.Kind.ToStorageName()).ToArray());
        AddArray(command, "id", NpgsqlDbType.Bigint, effects.Select(static e => StorageId(e.Token.TokenId)).ToArray());
        AddArray(command, "order", NpgsqlDbType.Integer, effects.Select(static e => e.Id.IntraRoundOrder).ToArray());
        AddArray(command, "ordinal", NpgsqlDbType.Integer, effects.Select(static e => e.Id.Ordinal).ToArray());
        AddArray(command, "account", NpgsqlDbType.Bytea, effects.Select(static e => e.Account.ToArray()).ToArray());
        AddArray(command, "txid", NpgsqlDbType.Text, effects.Select(static e => e.Id.TransactionId).ToArray());
        AddArray(command, "type", NpgsqlDbType.Text, effects.Select(static e => e.Type.ToString()).ToArray());
        AddArray(command, "delta", NpgsqlDbType.Text, effects.Select(static e => e.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertHoldingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ulong round, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken)
    {
        // The last effect of a round per account/token carries the end-of-round absolute balance.
        foreach (var batch in effects
            .GroupBy(static e => new HoldingKey(e.Account, e.Token))
            .Select(static g => g.MaxBy(static e => e.Id.Ordinal)!)
            .Chunk(WriteBatchSize))
        {
            await UpsertHoldingsBatchAsync(connection, transaction, round, batch, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpsertHoldingsBatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ulong round, TokenEffect[] latest, CancellationToken cancellationToken)
    {

        const string sql = """
            INSERT INTO holdings_by_account (account, token_kind, token_id, amount, opted_in, last_changed_round)
            SELECT u.account, u.token_kind, u.token_id, u.amount::numeric, u.opted_in, @round
            FROM unnest(@account, @kind, @id, @amount, @optedin) AS u(account, token_kind, token_id, amount, opted_in)
            ON CONFLICT (account, token_kind, token_id) DO UPDATE
              SET amount = EXCLUDED.amount,
                  opted_in = EXCLUDED.opted_in,
                  last_changed_round = EXCLUDED.last_changed_round
              WHERE holdings_by_account.last_changed_round <= EXCLUDED.last_changed_round
            """;

        // The token-oriented copy is written with the same statement shape, in the same transaction.
        const string byTokenSql = """
            INSERT INTO holdings_by_token (token_kind, token_id, account, amount, last_changed_round)
            SELECT u.token_kind, u.token_id, u.account, u.amount::numeric, @round
            FROM unnest(@account, @kind, @id, @amount) AS u(account, token_kind, token_id, amount)
            ON CONFLICT (token_kind, token_id, account) DO UPDATE
              SET amount = EXCLUDED.amount,
                  last_changed_round = EXCLUDED.last_changed_round
              WHERE holdings_by_token.last_changed_round <= EXCLUDED.last_changed_round
            """;

        var accounts = latest.Select(static e => e.Account.ToArray()).ToArray();
        var kinds = latest.Select(static e => e.Token.Kind.ToStorageName()).ToArray();
        var ids = latest.Select(static e => StorageId(e.Token.TokenId)).ToArray();
        var amounts = latest.Select(static e => e.BalanceAfter.ToString()).ToArray();

        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("round", unchecked((long)round));
            AddArray(command, "account", NpgsqlDbType.Bytea, accounts);
            AddArray(command, "kind", NpgsqlDbType.Text, kinds);
            AddArray(command, "id", NpgsqlDbType.Bigint, ids);
            AddArray(command, "amount", NpgsqlDbType.Text, amounts);
            AddArray(command, "optedin", NpgsqlDbType.Boolean, latest.Select(static e => e.Type != EffectType.AssetOptOut).ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(byTokenSql, connection, transaction))
        {
            command.Parameters.AddWithValue("round", unchecked((long)round));
            AddArray(command, "account", NpgsqlDbType.Bytea, accounts);
            AddArray(command, "kind", NpgsqlDbType.Text, kinds);
            AddArray(command, "id", NpgsqlDbType.Bigint, ids);
            AddArray(command, "amount", NpgsqlDbType.Text, amounts);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpsertTouchedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ulong round, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken)
    {
        foreach (var batch in effects.Select(static e => new HoldingKey(e.Account, e.Token)).Distinct().Chunk(WriteBatchSize))
        {
            await UpsertTouchedBatchAsync(connection, transaction, round, batch, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpsertTouchedBatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ulong round, HoldingKey[] touched, CancellationToken cancellationToken)
    {

        const string sql = """
            INSERT INTO accounts_touched_by_token (token_kind, token_id, account, first_round, last_round)
            SELECT u.token_kind, u.token_id, u.account, @round, @round
            FROM unnest(@kind, @id, @account) AS u(token_kind, token_id, account)
            ON CONFLICT (token_kind, token_id, account) DO UPDATE
              SET first_round = LEAST(accounts_touched_by_token.first_round, EXCLUDED.first_round),
                  last_round = GREATEST(accounts_touched_by_token.last_round, EXCLUDED.last_round)
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("round", unchecked((long)round));
        AddArray(command, "kind", NpgsqlDbType.Text, touched.Select(static k => k.Token.Kind.ToStorageName()).ToArray());
        AddArray(command, "id", NpgsqlDbType.Bigint, touched.Select(static k => StorageId(k.Token.TokenId)).ToArray());
        AddArray(command, "account", NpgsqlDbType.Bytea, touched.Select(static k => k.Account.ToArray()).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertCursorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ChainStatus status, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO chain_status (chain, indexed_round, indexed_block_hash, target_round, status, updated_at)
            VALUES (@chain, @round, @hash, @target, @status, @updated)
            ON CONFLICT (chain) DO UPDATE
              SET indexed_round = EXCLUDED.indexed_round,
                  indexed_block_hash = EXCLUDED.indexed_block_hash,
                  target_round = EXCLUDED.target_round,
                  status = EXCLUDED.status,
                  updated_at = EXCLUDED.updated_at
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("chain", status.Chain);
        command.Parameters.AddWithValue("round", unchecked((long)status.IndexedRound));
        command.Parameters.AddWithValue("hash", status.IndexedBlockHash.ToArray());
        command.Parameters.AddWithValue("target", status.TargetRound is { } target ? unchecked((long)target) : DBNull.Value);
        command.Parameters.AddWithValue("status", status.State.ToString());
        command.Parameters.AddWithValue("updated", status.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Token ids are stored as signed bigint; ids beyond its range would sort wrongly, so they are refused.</summary>
    private static long StorageId(ulong tokenId) =>
        tokenId <= long.MaxValue
            ? (long)tokenId
            : throw new ArgumentOutOfRangeException(nameof(tokenId), tokenId, "Token ids above 2^63 - 1 are not supported by the PostgreSQL provider.");

    private static void AddArray<T>(NpgsqlCommand command, string name, NpgsqlDbType elementType, T[] values) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | elementType) { Value = values });

    private void EnsureChain(string chain)
    {
        if (!string.Equals(chain, _chain, StringComparison.Ordinal))
        {
            throw new ArgumentException($"This storage serves chain {_chain} but was asked for {chain}.", nameof(chain));
        }
    }
}
