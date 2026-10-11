using System.Data;
using System.Globalization;
using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Npgsql;
using NpgsqlTypes;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage
{
    private const int MaxPageSize = 1000;

    public async Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken)
    {
        EnsureChain(chain);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT indexed_round, indexed_block_hash, target_round, status, updated_at FROM chain_status WHERE chain = @chain", connection);
        command.Parameters.AddWithValue("chain", chain);
        NpgsqlDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The worker creates the schema; until it has, nothing is indexed.
            return null;
        }

        await using var _ = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ChainStatus(
            chain,
            (ulong)reader.GetInt64(0),
            (byte[])reader[1],
            reader.IsDBNull(2) ? null : (ulong)reader.GetInt64(2),
            Enum.Parse<IngestionState>(reader.GetString(3)),
            new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero));
    }

    public async Task UpdateProgressAsync(ChainStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);
        EnsureChain(status.Chain);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "UPDATE chain_status SET target_round = @target, status = @status, updated_at = @updated WHERE chain = @chain AND indexed_round = @round",
            connection);
        command.Parameters.AddWithValue("chain", status.Chain);
        command.Parameters.AddWithValue("round", unchecked((long)status.IndexedRound));
        command.Parameters.Add(new NpgsqlParameter("target", NpgsqlDbType.Bigint) { Value = status.TargetRound is { } t ? unchecked((long)t) : DBNull.Value });
        command.Parameters.AddWithValue("status", status.State.ToString());
        command.Parameters.AddWithValue("updated", status.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TokenInfo?> GetAsync(TokenKey key, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT creator, decimals, symbol, name, standard, standard_verified, controlling_app_id,
                   created_round, deleted_round, metadata_json::text
            FROM token WHERE token_kind = @kind AND token_id = @id
            """,
            connection);
        command.Parameters.AddWithValue("kind", key.Kind.ToStorageName());
        command.Parameters.AddWithValue("id", StorageId(key.TokenId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TokenInfo(
            key,
            reader.IsDBNull(0) ? null : new Address((byte[])reader[0]),
            reader.IsDBNull(1) ? null : reader.GetInt16(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            Enum.Parse<TokenStandard>(reader.GetString(4)),
            reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : (ulong)reader.GetInt64(6),
            reader.IsDBNull(7) ? null : (ulong)reader.GetInt64(7),
            reader.IsDBNull(8) ? null : (ulong)reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }

    public async Task UpsertAsync(TokenInfo token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO token (token_kind, token_id, creator, decimals, symbol, name, standard, standard_verified,
                               controlling_app_id, created_round, deleted_round, metadata_json)
            VALUES (@kind, @id, @creator, @decimals, @symbol, @name, @standard, @verified, @app, @created, @deleted, @meta::jsonb)
            ON CONFLICT (token_kind, token_id) DO UPDATE SET
              creator = EXCLUDED.creator, decimals = EXCLUDED.decimals, symbol = EXCLUDED.symbol, name = EXCLUDED.name,
              standard = EXCLUDED.standard, standard_verified = EXCLUDED.standard_verified,
              controlling_app_id = EXCLUDED.controlling_app_id, created_round = EXCLUDED.created_round,
              deleted_round = EXCLUDED.deleted_round, metadata_json = EXCLUDED.metadata_json
            """,
            connection);
        command.Parameters.AddWithValue("kind", token.Key.Kind.ToStorageName());
        command.Parameters.AddWithValue("id", StorageId(token.Key.TokenId));
        command.Parameters.Add(new NpgsqlParameter("creator", NpgsqlDbType.Bytea) { Value = (object?)token.Creator?.ToArray() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("decimals", NpgsqlDbType.Smallint) { Value = token.Decimals is { } d ? (short)d : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("symbol", NpgsqlDbType.Text) { Value = (object?)token.Symbol ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = (object?)token.Name ?? DBNull.Value });
        command.Parameters.AddWithValue("standard", token.Standard.ToString());
        command.Parameters.AddWithValue("verified", token.StandardVerified);
        command.Parameters.Add(new NpgsqlParameter("app", NpgsqlDbType.Bigint) { Value = token.ControllingAppId is { } a ? unchecked((long)a) : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.Bigint) { Value = token.CreatedRound is { } c ? unchecked((long)c) : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("deleted", NpgsqlDbType.Bigint) { Value = token.DeletedRound is { } x ? unchecked((long)x) : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("meta", NpgsqlDbType.Text) { Value = (object?)token.MetadataJson ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<HoldingKey, Holding>> GetCurrentHoldingsAsync(IReadOnlyCollection<HoldingKey> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new Dictionary<HoldingKey, Holding>(keys.Count);
        if (keys.Count == 0)
        {
            return result;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT h.account, h.token_kind, h.token_id, h.amount, h.opted_in, h.last_changed_round
            FROM unnest(@account, @kind, @id) AS k(account, token_kind, token_id)
            JOIN holdings_by_account h USING (account, token_kind, token_id)
            """,
            connection);
        var list = keys.ToArray();
        AddArray(command, "account", NpgsqlDbType.Bytea, list.Select(static k => k.Account.ToArray()).ToArray());
        AddArray(command, "kind", NpgsqlDbType.Text, list.Select(static k => k.Token.Kind.ToStorageName()).ToArray());
        AddArray(command, "id", NpgsqlDbType.Bigint, list.Select(static k => StorageId(k.Token.TokenId)).ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var holding = ReadHolding(reader);
            result[new HoldingKey(holding.Account, holding.Token)] = holding;
        }

        return result;
    }

    public async Task<Page<Holding>> GetHoldingsAsync(Address account, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        var (afterKind, afterId) = ParseHoldingCursor(cursor);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        var indexed = await ReadIndexedRoundAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            """
            SELECT account, token_kind, token_id, amount, opted_in, last_changed_round
            FROM holdings_by_account
            WHERE account = @account
              AND (@includeZero OR amount > 0)
              AND (token_kind, token_id) > (@afterKind, @afterId)
            ORDER BY token_kind, token_id
            LIMIT @limit
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("account", account.ToArray());
        command.Parameters.AddWithValue("includeZero", includeZero);
        command.Parameters.AddWithValue("afterKind", afterKind);
        command.Parameters.AddWithValue("afterId", afterId);
        command.Parameters.AddWithValue("limit", pageSize + 1);

        var items = new List<Holding>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadHolding(reader));
            }
        }

        return ToPage(items, pageSize, indexed, static h => $"{h.Token.Kind.ToStorageName()}:{h.Token.TokenId.ToString(CultureInfo.InvariantCulture)}");
    }

    public async Task<Amount> GetBalanceAtRoundAsync(Address account, TokenKey token, ulong round, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        var indexed = await ReadIndexedRoundAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (round > indexed)
        {
            throw new RoundNotIndexedException(round, indexed);
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT balance_after FROM effects_by_account_token
            WHERE account = @account AND token_kind = @kind AND token_id = @id AND round <= @round
            ORDER BY round DESC, intra_round_order DESC, ordinal DESC
            LIMIT 1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("account", account.ToArray());
        command.Parameters.AddWithValue("kind", token.Kind.ToStorageName());
        command.Parameters.AddWithValue("id", StorageId(token.TokenId));
        command.Parameters.AddWithValue("round", unchecked((long)round));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is decimal balance ? ToAmount(balance) : Amount.Zero;
    }

    public async Task<Page<Holding>> GetHoldersAtRoundAsync(TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        var after = ParseHolderCursor(cursor);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        var indexed = await ReadIndexedRoundAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (round > indexed)
        {
            throw new RoundNotIndexedException(round, indexed);
        }

        // Pages over the accounts that ever touched the token and reads each one's latest balance at or before the round
        // from its own effect history (one index lookup per account). Cost is proportional to the accounts scanned to fill the
        // page, which is the page size plus any zero-balance accounts skipped when zero balances are excluded.
        await using var command = new NpgsqlCommand(
            """
            SELECT t.account, COALESCE(b.balance_after, 0), COALESCE(b.round, t.first_round)
            FROM accounts_touched_by_token t
            LEFT JOIN LATERAL (
                SELECT e.balance_after, e.round
                FROM effects_by_account_token e
                WHERE e.account = t.account AND e.token_kind = t.token_kind AND e.token_id = t.token_id AND e.round <= @round
                ORDER BY e.round DESC, e.intra_round_order DESC, e.ordinal DESC
                LIMIT 1
            ) b ON true
            WHERE t.token_kind = @kind AND t.token_id = @id AND t.first_round <= @round AND t.account > @after
              AND (@includeZero OR COALESCE(b.balance_after, 0) > 0)
            ORDER BY t.account
            LIMIT @limit
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("round", unchecked((long)round));
        command.Parameters.AddWithValue("kind", token.Kind.ToStorageName());
        command.Parameters.AddWithValue("id", StorageId(token.TokenId));
        command.Parameters.AddWithValue("after", after);
        command.Parameters.AddWithValue("includeZero", includeZero);
        command.Parameters.AddWithValue("limit", pageSize + 1);

        var items = new List<Holding>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new Holding(new Address((byte[])reader[0]), token, ToAmount(reader.GetDecimal(1)), true, (ulong)reader.GetInt64(2)));
            }
        }

        return ToPage(items, pageSize, indexed, static h => Convert.ToHexString(h.Account.Bytes));
    }

    public async Task<Page<TokenEffect>> GetEffectsAsync(Address account, TokenKey token, ulong? fromRound, ulong? toRound, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        var (cursorRound, cursorOrder, cursorOrdinal) = ParseEffectCursor(cursor);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
        var indexed = await ReadIndexedRoundAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            """
            SELECT round, intra_round_order, ordinal, transaction_id, inner_path, effect_type, delta, balance_after, counterparty
            FROM effects_by_account_token
            WHERE account = @account AND token_kind = @kind AND token_id = @id
              AND round >= @from AND round <= @to
              AND (round, intra_round_order, ordinal) < (@cr, @co, @cn)
            ORDER BY round DESC, intra_round_order DESC, ordinal DESC
            LIMIT @limit
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("account", account.ToArray());
        command.Parameters.AddWithValue("kind", token.Kind.ToStorageName());
        command.Parameters.AddWithValue("id", StorageId(token.TokenId));
        // A start beyond the signed column range lies past every indexed round, so it matches nothing.
        command.Parameters.AddWithValue("from", (long)Math.Min(fromRound ?? 0, long.MaxValue));
        command.Parameters.AddWithValue("to", unchecked((long)Math.Min(toRound ?? indexed, indexed)));
        command.Parameters.AddWithValue("cr", cursorRound);
        command.Parameters.AddWithValue("co", cursorOrder);
        command.Parameters.AddWithValue("cn", cursorOrdinal);
        command.Parameters.AddWithValue("limit", pageSize + 1);

        var items = new List<TokenEffect>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var path = (int[])reader[4];
                var id = new EffectId((ulong)reader.GetInt64(0), reader.GetInt32(1), reader.GetString(3), path, reader.GetInt32(2));
                items.Add(new TokenEffect(
                    id,
                    account,
                    token,
                    Enum.Parse<EffectType>(reader.GetString(5)),
                    new BigInteger(reader.GetDecimal(6)),
                    ToAmount(reader.GetDecimal(7)),
                    reader.IsDBNull(8) ? null : new Address((byte[])reader[8])));
            }
        }

        return ToPage(
            items,
            pageSize,
            indexed,
            static e => string.Create(CultureInfo.InvariantCulture, $"{e.Id.Round}:{e.Id.IntraRoundOrder}:{e.Id.Ordinal}"));
    }

    private static Holding ReadHolding(NpgsqlDataReader reader)
    {
        var storedKind = reader.GetString(1);
        if (!TokenKindExtensions.TryParseStorageName(storedKind, out var kind))
        {
            throw new InvalidDataException($"The database holds an unknown token kind \"{storedKind}\".");
        }

        return new Holding(
            new Address((byte[])reader[0]),
            new TokenKey(kind, (ulong)reader.GetInt64(2)),
            ToAmount(reader.GetDecimal(3)),
            reader.GetBoolean(4),
            (ulong)reader.GetInt64(5));
    }

    private static Amount ToAmount(decimal value) => new(new BigInteger(value));

    private static int ClampLimit(int limit) => limit < 1 ? 1 : Math.Min(limit, MaxPageSize);

    private static Page<T> ToPage<T>(List<T> fetched, int pageSize, ulong indexed, Func<T, string> cursorOf)
    {
        if (fetched.Count <= pageSize)
        {
            return new Page<T>(fetched, null, indexed);
        }

        fetched.RemoveAt(fetched.Count - 1);
        return new Page<T>(fetched, cursorOf(fetched[^1]), indexed);
    }

    private static byte[] ParseHolderCursor(string? cursor)
    {
        if (cursor is null)
        {
            return [];
        }

        try
        {
            return Convert.FromHexString(cursor);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Invalid cursor.", nameof(cursor), ex);
        }
    }

    private static (string Kind, long Id) ParseHoldingCursor(string? cursor)
    {
        if (cursor is null)
        {
            return (string.Empty, -1);
        }

        var parts = cursor.Split(':');
        return parts.Length == 2 && TokenKindExtensions.TryParseStorageName(parts[0], out _)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? (parts[0], id)
            : throw new ArgumentException("Invalid cursor.", nameof(cursor));
    }

    private static (long Round, int Order, int Ordinal) ParseEffectCursor(string? cursor)
    {
        if (cursor is null)
        {
            return (long.MaxValue, int.MaxValue, int.MaxValue);
        }

        var parts = cursor.Split(':');
        return parts.Length == 3
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var round)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var order)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            ? (round, order, ordinal)
            : throw new ArgumentException("Invalid cursor.", nameof(cursor));
    }

    private static async Task<NpgsqlTransaction> BeginSnapshotAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ulong> ReadIndexedRoundAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT indexed_round FROM chain_status WHERE chain = @chain", connection, transaction);
        command.Parameters.AddWithValue("chain", _chain);
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is long round
                ? (ulong)round
                : throw new ChainNotStartedException(_chain);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The worker has not created the schema yet.
            throw new ChainNotStartedException(_chain);
        }
    }
}
