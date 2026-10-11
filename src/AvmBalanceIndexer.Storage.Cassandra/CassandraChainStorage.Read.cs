using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage
{
    public async Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken)
    {
        EnsureChain(chain);
        RowSet rows;
        try
        {
            var statement = await BindAsync(
                $"SELECT indexed_round, indexed_block_hash, target_round, status, updated_at FROM {Table("chain_status")} WHERE chain = ?",
                cancellationToken,
                chain).ConfigureAwait(false);
            rows = await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidQueryException ex) when (IsMissingSchema(ex))
        {
            // The worker creates the keyspace and tables; until it has, nothing is indexed.
            return null;
        }

        var row = rows.FirstOrDefault();
        return row is null
            ? null
            : new ChainStatus(
                chain,
                (ulong)row.GetValue<long>("indexed_round"),
                row.GetValue<byte[]>("indexed_block_hash"),
                row.IsNull("target_round") ? null : (ulong)row.GetValue<long>("target_round"),
                Enum.Parse<IngestionState>(row.GetValue<string>("status")),
                row.GetValue<DateTimeOffset>("updated_at"));
    }

    public async Task UpdateProgressAsync(ChainStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);
        EnsureChain(status.Chain);
        var statement = await BindAsync(
            $"UPDATE {Table("chain_status")} SET target_round = ?, status = ?, updated_at = ? WHERE chain = ? IF indexed_round = ?",
            cancellationToken,
            status.TargetRound is { } target ? unchecked((long)target) : null,
            status.State.ToString(),
            status.UpdatedAt,
            status.Chain,
            unchecked((long)status.IndexedRound)).ConfigureAwait(false);
        statement.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
        await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TokenInfo?> GetAsync(TokenKey key, CancellationToken cancellationToken)
    {
        var rows = await ExecuteAsync(
            $"""
            SELECT creator, decimals, symbol, name, standard, standard_verified, controlling_app_id, created_round, deleted_round, metadata_json
            FROM {Table("token_by_id")} WHERE token_kind = ? AND token_id = ?
            """,
            cancellationToken,
            key.Kind.ToStorageName(),
            StorageId(key.TokenId)).ConfigureAwait(false);
        var row = rows.FirstOrDefault();
        return row is null
            ? null
            : new TokenInfo(
                key,
                row.IsNull("creator") ? null : new Address(row.GetValue<byte[]>("creator")),
                row.IsNull("decimals") ? null : row.GetValue<short>("decimals"),
                row.IsNull("symbol") ? null : row.GetValue<string>("symbol"),
                row.IsNull("name") ? null : row.GetValue<string>("name"),
                Enum.Parse<TokenStandard>(row.GetValue<string>("standard")),
                row.GetValue<bool>("standard_verified"),
                row.IsNull("controlling_app_id") ? null : (ulong)row.GetValue<long>("controlling_app_id"),
                row.IsNull("created_round") ? null : (ulong)row.GetValue<long>("created_round"),
                row.IsNull("deleted_round") ? null : (ulong)row.GetValue<long>("deleted_round"),
                row.IsNull("metadata_json") ? null : row.GetValue<string>("metadata_json"));
    }

    public async Task UpsertAsync(TokenInfo token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        await ExecuteAsync(
            $"""
            INSERT INTO {Table("token_by_id")} (token_kind, token_id, creator, decimals, symbol, name, standard, standard_verified,
                                                controlling_app_id, created_round, deleted_round, metadata_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            cancellationToken,
            token.Key.Kind.ToStorageName(),
            StorageId(token.Key.TokenId),
            token.Creator?.ToArray(),
            token.Decimals is { } decimals ? (short)decimals : null,
            token.Symbol,
            token.Name,
            token.Standard.ToString(),
            token.StandardVerified,
            token.ControllingAppId is { } app ? unchecked((long)app) : null,
            token.CreatedRound is { } created ? unchecked((long)created) : null,
            token.DeletedRound is { } deleted ? unchecked((long)deleted) : null,
            token.MetadataJson).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<HoldingKey, Holding>> GetCurrentHoldingsAsync(IReadOnlyCollection<HoldingKey> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new Dictionary<HoldingKey, Holding>(keys.Count);
        if (keys.Count == 0)
        {
            return result;
        }

        var indexed = await TryReadIndexedRoundAsync(cancellationToken).ConfigureAwait(false);
        if (indexed is null)
        {
            // Nothing is published (or the schema does not exist yet), so no holding can be visible.
            return result;
        }

        var distinct = keys.Distinct().ToArray();
        var holdings = await RunBoundedAsync(
            distinct,
            async key =>
            {
                var rows = await ExecuteAsync(
                    $"""
                    SELECT account, token_kind, token_id, amount, opted_in, last_changed_round FROM {Table("holdings_by_account")}
                    WHERE account = ? AND token_kind = ? AND token_id = ?
                    """,
                    cancellationToken,
                    key.Account.ToArray(),
                    key.Token.Kind.ToStorageName(),
                    StorageId(key.Token.TokenId)).ConfigureAwait(false);
                return rows.FirstOrDefault() is { } row ? await ResolvePublishedAsync(ReadHolding(row), indexed.Value, cancellationToken).ConfigureAwait(false) : null;
            },
            cancellationToken).ConfigureAwait(false);
        foreach (var holding in holdings)
        {
            if (holding is not null)
            {
                result[new HoldingKey(holding.Account, holding.Token)] = holding;
            }
        }

        return result;
    }

    public Task<Page<Holding>> GetHoldingsAsync(Address account, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken) =>
        TranslateTimeoutsAsync(() => GetHoldingsCoreAsync(account, includeZero, cursor, limit, cancellationToken));

    private async Task<Page<Holding>> GetHoldingsCoreAsync(Address account, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        var (afterKind, afterId) = ParseHoldingCursor(cursor);
        var indexed = await ReadIndexedRoundAsync(cancellationToken).ConfigureAwait(false);

        // Zero balances cannot be filtered by the server (amounts are text), so chunks are read until the page is full.
        var items = new List<Holding>();
        var exhausted = false;
        while (!exhausted && items.Count <= pageSize)
        {
            var rows = (await ExecuteAsync(
                $"""
                SELECT account, token_kind, token_id, amount, opted_in, last_changed_round FROM {Table("holdings_by_account")}
                WHERE account = ? AND (token_kind, token_id) > (?, ?) LIMIT ?
                """,
                cancellationToken,
                account.ToArray(),
                afterKind,
                afterId,
                pageSize + 1).ConfigureAwait(false)).ToArray();
            exhausted = rows.Length < pageSize + 1;
            foreach (var row in rows)
            {
                var stored = ReadHolding(row);
                afterKind = stored.Token.Kind.ToStorageName();
                afterId = StorageId(stored.Token.TokenId);
                if (await ResolvePublishedAsync(stored, indexed, cancellationToken).ConfigureAwait(false) is { } holding
                    && (includeZero || !holding.Amount.IsZero))
                {
                    items.Add(holding);
                }

                if (items.Count > pageSize)
                {
                    break;
                }
            }
        }

        return ToPage(items, pageSize, indexed, static h => $"{h.Token.Kind.ToStorageName()}:{h.Token.TokenId.ToString(CultureInfo.InvariantCulture)}");
    }

    public Task<Amount> GetBalanceAtRoundAsync(Address account, TokenKey token, ulong round, CancellationToken cancellationToken) =>
        TranslateTimeoutsAsync(() => GetBalanceAtRoundCoreAsync(account, token, round, cancellationToken));

    private async Task<Amount> GetBalanceAtRoundCoreAsync(Address account, TokenKey token, ulong round, CancellationToken cancellationToken)
    {
        var indexed = await ReadIndexedRoundAsync(cancellationToken).ConfigureAwait(false);
        if (round > indexed)
        {
            throw new RoundNotIndexedException(round, indexed);
        }

        return (await LatestEffectAtOrBeforeAsync(account, token, round, cancellationToken).ConfigureAwait(false))?.Balance ?? Amount.Zero;
    }

    public Task<Page<Holding>> GetHoldersAtRoundAsync(TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken) =>
        TranslateTimeoutsAsync(() => GetHoldersAtRoundCoreAsync(token, round, includeZero, cursor, limit, cancellationToken));

    private async Task<Page<Holding>> GetHoldersAtRoundCoreAsync(TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        var after = ParseHolderCursor(cursor);
        var indexed = await ReadIndexedRoundAsync(cancellationToken).ConfigureAwait(false);
        if (round > indexed)
        {
            throw new RoundNotIndexedException(round, indexed);
        }

        // The accounts that ever touched the token are spread over TokenShards partitions, each sorted by account. Every shard
        // is scanned lazily and the shards are merged by account, so the cost follows the page size and not the holder count.
        // One gate bounds the lookups of all shards together.
        var chunk = Math.Clamp(2 * (pageSize + 1) / TokenShards, 2, 256);
        using var gate = new SemaphoreSlim(MaxInFlight);
        var shards = new List<IAsyncEnumerator<Holding>>(TokenShards);
        var starting = new List<Task<bool>>(TokenShards);
        try
        {
            for (var shard = 0; shard < TokenShards; shard++)
            {
                var scan = (round == indexed
                    ? ScanShardLatestAsync(token, (short)shard, indexed, includeZero, after, chunk, gate, cancellationToken)
                    : ScanShardAsync(token, (short)shard, round, includeZero, after, chunk, gate, cancellationToken)).GetAsyncEnumerator(cancellationToken);
                shards.Add(scan);
                starting.Add(scan.MoveNextAsync().AsTask());
            }

            var heads = await Task.WhenAll(starting).ConfigureAwait(false);
            var items = new List<Holding>();
            while (items.Count <= pageSize)
            {
                var next = -1;
                for (var i = 0; i < shards.Count; i++)
                {
                    if (heads[i] && (next < 0 || shards[i].Current.Account.Bytes.SequenceCompareTo(shards[next].Current.Account.Bytes) < 0))
                    {
                        next = i;
                    }
                }

                if (next < 0)
                {
                    break;
                }

                items.Add(shards[next].Current);
                if (items.Count > pageSize)
                {
                    break;
                }

                heads[next] = await shards[next].MoveNextAsync().ConfigureAwait(false);
            }

            return ToPage(items, pageSize, indexed, static h => Convert.ToHexString(h.Account.Bytes));
        }
        finally
        {
            // An iterator must not be disposed while a MoveNext is still running, which is the case when another shard faulted.
            foreach (var pending in starting)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The first failure is already propagating.
                }
            }

            foreach (var shard in shards)
            {
                await shard.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Holders at the indexed round are the current holdings, read from the token-oriented copy without per-account lookups.</summary>
    private async IAsyncEnumerable<Holding> ScanShardLatestAsync(
        TokenKey token,
        short shard,
        ulong indexed,
        bool includeZero,
        byte[] after,
        int chunk,
        SemaphoreSlim gate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = after;
        while (true)
        {
            var rows = (await ExecuteAsync(
                $"SELECT account, amount, last_changed_round FROM {Table("holdings_by_token")} WHERE token_kind = ? AND token_id = ? AND shard = ? AND account > ? LIMIT ?",
                cancellationToken,
                token.Kind.ToStorageName(),
                StorageId(token.TokenId),
                shard,
                cursor,
                chunk).ConfigureAwait(false)).ToArray();
            if (rows.Length == 0)
            {
                yield break;
            }

            cursor = rows[^1].GetValue<byte[]>("account");
            var resolved = await RunBoundedAsync(
                rows,
                async row =>
                {
                    var account = new Address(row.GetValue<byte[]>("account"));
                    var last = (ulong)row.GetValue<long>("last_changed_round");
                    if (last <= indexed)
                    {
                        return new Holding(account, token, Amount.Parse(row.GetValue<string>("amount")), true, last);
                    }

                    // Written by a round that is not published (crashed or losing writer): the effect history decides.
                    return await LatestEffectAtOrBeforeAsync(account, token, indexed, cancellationToken).ConfigureAwait(false) is { } latest
                        ? new Holding(account, token, latest.Balance, true, latest.Round)
                        : null;
                },
                cancellationToken,
                gate).ConfigureAwait(false);
            foreach (var holding in resolved)
            {
                if (holding is not null && (includeZero || !holding.Amount.IsZero))
                {
                    yield return holding;
                }
            }

            if (rows.Length < chunk)
            {
                yield break;
            }
        }
    }

    private async IAsyncEnumerable<Holding> ScanShardAsync(
        TokenKey token,
        short shard,
        ulong round,
        bool includeZero,
        byte[] after,
        int chunk,
        SemaphoreSlim gate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = after;
        while (true)
        {
            var touched = (await ExecuteAsync(
                $"SELECT account, first_round FROM {Table("accounts_touched_by_token")} WHERE token_kind = ? AND token_id = ? AND shard = ? AND account > ? LIMIT ?",
                cancellationToken,
                token.Kind.ToStorageName(),
                StorageId(token.TokenId),
                shard,
                cursor,
                chunk).ConfigureAwait(false)).ToArray();
            if (touched.Length == 0)
            {
                yield break;
            }

            cursor = touched[^1].GetValue<byte[]>("account");
            var candidates = touched
                .Where(row => (ulong)row.GetValue<long>("first_round") <= round)
                .Select(static row => (Account: new Address(row.GetValue<byte[]>("account")), FirstRound: (ulong)row.GetValue<long>("first_round")))
                .ToArray();
            var latest = await RunBoundedAsync(
                candidates,
                candidate => LatestEffectAtOrBeforeAsync(candidate.Account, token, round, cancellationToken),
                cancellationToken,
                gate).ConfigureAwait(false);
            for (var i = 0; i < candidates.Length; i++)
            {
                var balance = latest[i]?.Balance ?? Amount.Zero;
                if (includeZero || !balance.IsZero)
                {
                    yield return new Holding(candidates[i].Account, token, balance, true, latest[i]?.Round ?? candidates[i].FirstRound);
                }
            }

            if (touched.Length < chunk)
            {
                yield break;
            }
        }
    }

    public Task<Page<TokenEffect>> GetEffectsAsync(Address account, TokenKey token, ulong? fromRound, ulong? toRound, string? cursor, int limit, CancellationToken cancellationToken) =>
        TranslateTimeoutsAsync(() => GetEffectsCoreAsync(account, token, fromRound, toRound, cursor, limit, cancellationToken));

    private async Task<Page<TokenEffect>> GetEffectsCoreAsync(Address account, TokenKey token, ulong? fromRound, ulong? toRound, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        var cursorKey = ParseEffectCursor(cursor);
        var indexed = await ReadIndexedRoundAsync(cancellationToken).ConfigureAwait(false);

        var upper = Math.Min(toRound ?? indexed, indexed);
        var lower = fromRound ?? 0;
        var items = new List<TokenEffect>();
        if (lower <= upper)
        {
            // A cursor below the upper bound narrows the first bucket with a tuple comparison; CQL cannot combine that with a
            // plain round bound on the same column, so the lower bound is applied while reading the newest-first rows.
            var resumeAt = cursorKey is { } key && key.Round <= upper ? key : (EffectCursor?)null;
            var topBucket = RoundBuckets.ForRound(resumeAt?.Round ?? upper);
            var bottomBucket = RoundBuckets.ForRound(lower);
            var kind = token.Kind.ToStorageName();
            var tokenId = StorageId(token.TokenId);

            var buckets = (await ExecuteAsync(
                $"SELECT bucket FROM {Table("effect_buckets_by_account_token")} WHERE account = ? AND token_kind = ? AND token_id = ? AND bucket <= ? AND bucket >= ?",
                cancellationToken,
                account.ToArray(),
                kind,
                tokenId,
                topBucket,
                bottomBucket).ConfigureAwait(false)).Select(static row => row.GetValue<int>("bucket")).ToArray();

            var reachedLower = false;
            foreach (var bucket in buckets)
            {
                var need = pageSize + 1 - items.Count;
                RowSet rows;
                if (resumeAt is { } resume && bucket == topBucket)
                {
                    rows = await ExecuteAsync(
                        $"""
                        SELECT round, intra_round_order, ordinal, transaction_id, inner_path, effect_type, delta, balance_after, counterparty
                        FROM {Table("effects_by_account_token_bucket")}
                        WHERE account = ? AND token_kind = ? AND token_id = ? AND bucket = ? AND (round, intra_round_order, ordinal) < (?, ?, ?) LIMIT ?
                        """,
                        cancellationToken,
                        account.ToArray(),
                        kind,
                        tokenId,
                        bucket,
                        unchecked((long)resume.Round),
                        resume.Order,
                        resume.Ordinal,
                        need).ConfigureAwait(false);
                }
                else
                {
                    rows = await ExecuteAsync(
                        $"""
                        SELECT round, intra_round_order, ordinal, transaction_id, inner_path, effect_type, delta, balance_after, counterparty
                        FROM {Table("effects_by_account_token_bucket")}
                        WHERE account = ? AND token_kind = ? AND token_id = ? AND bucket = ? AND round <= ? LIMIT ?
                        """,
                        cancellationToken,
                        account.ToArray(),
                        kind,
                        tokenId,
                        bucket,
                        unchecked((long)upper),
                        need).ConfigureAwait(false);
                }

                foreach (var row in rows)
                {
                    var effectRound = (ulong)row.GetValue<long>("round");
                    if (effectRound < lower)
                    {
                        reachedLower = true;
                        break;
                    }

                    items.Add(ReadEffect(row, account, token));
                }

                if (items.Count > pageSize || reachedLower)
                {
                    break;
                }
            }
        }

        return ToPage(
            items,
            pageSize,
            indexed,
            static e => string.Create(CultureInfo.InvariantCulture, $"{e.Id.Round}:{e.Id.IntraRoundOrder}:{e.Id.Ordinal}"));
    }

    internal static async Task<T> TranslateTimeoutsAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationTimedOutException or ReadTimeoutException)
        {
            throw new StorageTimeoutException("The query exceeded the read timeout.", ex);
        }
    }

    private readonly record struct EffectCursor(ulong Round, int Order, int Ordinal);

    private readonly record struct LatestEffect(Amount Balance, ulong Round, EffectType Type);

    /// <summary>End-of-effect balance of the newest effect at or before the round, or null when the account never held the token by then.</summary>
    private async Task<LatestEffect?> LatestEffectAtOrBeforeAsync(Address account, TokenKey token, ulong round, CancellationToken cancellationToken)
    {
        var kind = token.Kind.ToStorageName();
        var tokenId = StorageId(token.TokenId);
        var buckets = await ExecuteAsync(
            $"SELECT bucket FROM {Table("effect_buckets_by_account_token")} WHERE account = ? AND token_kind = ? AND token_id = ? AND bucket <= ?",
            cancellationToken,
            account.ToArray(),
            kind,
            tokenId,
            RoundBuckets.ForRound(round)).ConfigureAwait(false);
        foreach (var bucketRow in buckets)
        {
            var rows = await ExecuteAsync(
                $"""
                SELECT round, balance_after, effect_type FROM {Table("effects_by_account_token_bucket")}
                WHERE account = ? AND token_kind = ? AND token_id = ? AND bucket = ? AND round <= ? LIMIT 1
                """,
                cancellationToken,
                account.ToArray(),
                kind,
                tokenId,
                bucketRow.GetValue<int>("bucket"),
                unchecked((long)round)).ConfigureAwait(false);
            if (rows.FirstOrDefault() is { } row)
            {
                return new LatestEffect(
                    Amount.Parse(row.GetValue<string>("balance_after")),
                    (ulong)row.GetValue<long>("round"),
                    Enum.Parse<EffectType>(row.GetValue<string>("effect_type")));
            }
        }

        return null;
    }

    /// <summary>
    /// A holding row written by a round that was not published yet (the writer crashed or lost the race) must not be seen;
    /// the effect history is authoritative for the published state.
    /// </summary>
    private async Task<Holding?> ResolvePublishedAsync(Holding stored, ulong indexed, CancellationToken cancellationToken)
    {
        if (stored.LastChangedRound <= indexed)
        {
            return stored;
        }

        return await LatestEffectAtOrBeforeAsync(stored.Account, stored.Token, indexed, cancellationToken).ConfigureAwait(false) is { } latest
            ? new Holding(stored.Account, stored.Token, latest.Balance, latest.Type != EffectType.AssetOptOut, latest.Round)
            : null;
    }

    private static Holding ReadHolding(Row row)
    {
        var storedKind = row.GetValue<string>("token_kind");
        if (!TokenKindExtensions.TryParseStorageName(storedKind, out var kind))
        {
            throw new InvalidDataException($"The database holds an unknown token kind \"{storedKind}\".");
        }

        return new Holding(
            new Address(row.GetValue<byte[]>("account")),
            new TokenKey(kind, (ulong)row.GetValue<long>("token_id")),
            Amount.Parse(row.GetValue<string>("amount")),
            row.GetValue<bool>("opted_in"),
            (ulong)row.GetValue<long>("last_changed_round"));
    }

    private static TokenEffect ReadEffect(Row row, Address account, TokenKey token) =>
        new(
            new EffectId(
                (ulong)row.GetValue<long>("round"),
                row.GetValue<int>("intra_round_order"),
                row.GetValue<string>("transaction_id"),
                row.IsNull("inner_path") ? [] : row.GetValue<int[]>("inner_path"),
                row.GetValue<int>("ordinal")),
            account,
            token,
            Enum.Parse<EffectType>(row.GetValue<string>("effect_type")),
            BigInteger.Parse(row.GetValue<string>("delta"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            Amount.Parse(row.GetValue<string>("balance_after")),
            row.IsNull("counterparty") ? null : new Address(row.GetValue<byte[]>("counterparty")));

    private async Task<ulong?> TryReadIndexedRoundAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await ReadCursorAsync(serial: false, cancellationToken).ConfigureAwait(false))?.Round;
        }
        catch (InvalidQueryException ex) when (IsMissingSchema(ex))
        {
            // The worker has not created the keyspace or tables yet.
            return null;
        }
    }

    private async Task<ulong> ReadIndexedRoundAsync(CancellationToken cancellationToken) =>
        await TryReadIndexedRoundAsync(cancellationToken).ConfigureAwait(false) ?? throw new ChainNotStartedException(_chain);

    private static int ClampLimit(int limit) => limit < 1 ? 1 : Math.Min(limit, MaxPageSize);

    private static Page<T> ToPage<T>(List<T> fetched, int pageSize, ulong indexed, Func<T, string> cursorOf)
    {
        if (fetched.Count <= pageSize)
        {
            return new Page<T>(fetched, null, indexed);
        }

        fetched.RemoveRange(pageSize, fetched.Count - pageSize);
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

    private static EffectCursor? ParseEffectCursor(string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        var parts = cursor.Split(':');
        return parts.Length == 3
            && ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var round) && round <= long.MaxValue
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var order)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            ? new EffectCursor(round, order, ordinal)
            : throw new ArgumentException("Invalid cursor.", nameof(cursor));
    }
}
