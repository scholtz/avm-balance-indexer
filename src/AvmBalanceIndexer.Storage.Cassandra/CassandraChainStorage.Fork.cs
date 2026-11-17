using System.Runtime.CompilerServices;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

/// <summary>
/// The partial rebuild of ADR 0021: this keyspace is filled from an earlier one up to a round, so that indexing continues after it. The
/// source is only read. Nothing is visible to readers until the last step publishes the cursor (a lightweight transaction on
/// <c>chain_status</c>), and every earlier row is an idempotent upsert, so a copy that stopped half way is repeated from its finished ranges.
/// </summary>
public sealed partial class CassandraChainStorage
{
    /// <summary>Ranges the Murmur3 ring is split into for every scanned table.</summary>
    private const int ForkRanges = 256;

    private const int ForkPageSize = 2_000;
    private const int ForkRangesInParallel = 8;
    private const string RequestStage = "request";

    public async Task<PartialRebuildResult> RunAsync(ReadOnlyMemory<byte> blockHashAtRound, IProgress<PartialRebuildProgress>? progress, CancellationToken cancellationToken)
    {
        var request = Request ?? throw new InvalidOperationException("No fork is configured (Storage:Cassandra:ForkFrom).");
        if (blockHashAtRound.Length != 32)
        {
            throw new ArgumentException("The hash of the block at the fork round (32 bytes) is required.", nameof(blockHashAtRound));
        }

        var session = await _source.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        var source = request.Source;
        var round = unchecked((long)request.Round);

        // The source must be a keyspace of this indexer that holds this chain. Reading its published round (and not its tip) matters: rows of a
        // round that was never published can exist in a keyspace (ADR 0003), and they must never be taken over.
        var sourceTables = await TablesOfAsync(session, source, cancellationToken).ConfigureAwait(false);
        if (sourceTables.Count == 0)
        {
            return Refused($"the source keyspace {source} does not exist");
        }

        if (!sourceTables.Contains("chain_status") || !sourceTables.Contains("effects_by_account_token_bucket"))
        {
            return Refused($"{source} is not a keyspace of this indexer (no chain_status or effect tables)");
        }

        var statuses = (await ExecuteAsync($"SELECT chain, indexed_round FROM {source}.chain_status", cancellationToken).ConfigureAwait(false)).ToArray();
        if (statuses.Length != 1 || !string.Equals(statuses[0].GetValue<string>("chain"), _chain, StringComparison.Ordinal))
        {
            return Refused($"{source} does not hold exactly the chain {_chain}");
        }

        var sourceRound = statuses[0].GetValue<long>("indexed_round");
        if (sourceRound < round)
        {
            return Refused($"{source} is at round {sourceRound}, before the requested round {request.Round}");
        }

        // Carried out = a published round AND the stored request matches. A keyspace that holds something else is refused, never continued:
        // rows between a corrected and an earlier round would stay, and holdings written with the higher timestamp would win.
        var published = await ReadCursorAsync(serial: true, cancellationToken).ConfigureAwait(false);
        var stored = await ReadForkRequestAsync(cancellationToken).ConfigureAwait(false);
        if (published is not null)
        {
            return stored is { } match && match.Source == source && match.Round == round
                ? new PartialRebuildResult(PartialRebuildOutcome.AlreadyDone, $"{_keyspace} was filled before.")
                : Refused($"{_keyspace} already follows the chain and was not filled from {source} at round {request.Round}");
        }

        if (stored is null)
        {
            var insert = await BindAsync(
                $"INSERT INTO {Table("fork_progress")} (stage, range, source, round) VALUES (?, 0, ?, ?) IF NOT EXISTS",
                cancellationToken,
                RequestStage,
                source,
                round).ConfigureAwait(false);
            insert.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
            if (!(await ExecuteAsync(insert, cancellationToken).ConfigureAwait(false)).First().GetValue<bool>("[applied]"))
            {
                stored = await ReadForkRequestAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                stored = (source, round);
            }
        }

        if (stored is not { } bound || bound.Source != source || bound.Round != round)
        {
            return Refused($"{_keyspace} holds an unfinished copy of {stored?.Source} at round {stored?.Round}; use a new keyspace name");
        }

        using var copy = new ForkCopy(source, round, _writeConcurrency, progress);
        await CopyStageAsync(copy, "effects_by_account_token_bucket", CopyAccountEffectsAsync, cancellationToken).ConfigureAwait(false);
        await CopyStageAsync(copy, "effects_by_token_bucket", CopyTokenEffectsAsync, cancellationToken).ConfigureAwait(false);
        await CopyStageAsync(copy, "accounts_touched_by_token", CopyTouchedAsync, cancellationToken).ConfigureAwait(false);
        await CopyStageAsync(copy, "holdings", RebuildHoldingsAsync, cancellationToken).ConfigureAwait(false);
        await CopyStageAsync(copy, "token_by_id", CopyTokensAsync, cancellationToken).ConfigureAwait(false);
        if (sourceTables.Contains("indexing_term"))
        {
            await CopyIndexingTermsAsync(copy, cancellationToken).ConfigureAwait(false);
        }

        // The publication: the same conditional insert the round writer uses for its first round. Before it the keyspace is invisible.
        var publish = await BindAsync(
            $"""
            INSERT INTO {Table("chain_status")} (chain, indexed_round, indexed_block_hash, target_round, status, updated_at)
            VALUES (?, ?, ?, ?, ?, ?) IF NOT EXISTS
            """,
            cancellationToken,
            _chain,
            round,
            blockHashAtRound.ToArray(),
            null,
            IngestionState.Backfilling.ToString(),
            DateTimeOffset.UtcNow).ConfigureAwait(false);
        publish.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
        var applied = (await ExecuteAsync(publish, cancellationToken).ConfigureAwait(false)).First().GetValue<bool>("[applied]");
        LastPublished = null;
        return applied
            ? new PartialRebuildResult(PartialRebuildOutcome.Done, string.Empty, copy.Effects, copy.Holdings)
            : new PartialRebuildResult(PartialRebuildOutcome.AlreadyDone, $"{_keyspace} was published by another instance.");

        static PartialRebuildResult Refused(string reason) => new(PartialRebuildOutcome.Refused, reason);
    }

    private async Task<(string Source, long Round)?> ReadForkRequestAsync(CancellationToken cancellationToken)
    {
        var row = (await ExecuteAsync($"SELECT source, round FROM {Table("fork_progress")} WHERE stage = ? AND range = 0", cancellationToken, RequestStage).ConfigureAwait(false)).FirstOrDefault();
        return row is null ? null : (row.GetValue<string>("source"), row.GetValue<long>("round"));
    }

    /// <summary>Runs one stage: every range that is not recorded as finished, eight at a time, and records each range after its last write.</summary>
    private async Task CopyStageAsync(ForkCopy copy, string stage, Func<ForkCopy, int, CancellationToken, Task<long>> copyRange, CancellationToken cancellationToken)
    {
        var done = (await ExecuteAsync($"SELECT range FROM {Table("fork_progress")} WHERE stage = ?", cancellationToken, stage).ConfigureAwait(false))
            .Select(static row => row.GetValue<int>("range"))
            .ToHashSet();
        var finished = done.Count;
        await RunBoundedAsync(
            Enumerable.Range(0, ForkRanges).Where(range => !done.Contains(range)).ToArray(),
            async range =>
            {
                var rows = await copyRange(copy, range, cancellationToken).ConfigureAwait(false);
                await ExecuteAsync($"INSERT INTO {Table("fork_progress")} (stage, range) VALUES (?, ?)", cancellationToken, stage, range).ConfigureAwait(false);
                copy.Report(stage, rows, Interlocked.Increment(ref finished), ForkRanges);
            },
            cancellationToken,
            ForkRangesInParallel).ConfigureAwait(false);
    }

    private async Task<long> CopyAccountEffectsAsync(ForkCopy copy, int range, CancellationToken cancellationToken)
    {
        long copied = 0;
        (byte[] Account, string Kind, long Id, int Bucket)? lastBucket = null;
        await foreach (var page in ScanAsync(copy.Source, "effects_by_account_token_bucket", "account, token_kind, token_id, bucket", "account, token_kind, token_id, bucket, round, intra_round_order, ordinal, transaction_id, inner_path, effect_type, delta, balance_after, counterparty", range, cancellationToken).ConfigureAwait(false))
        {
            var rows = page.Where(row => row.GetValue<long>("round") <= copy.Round).ToArray();
            await GatedAsync(copy.Gate, rows, row => InsertAccountEffectRowAsync(
                row.GetValue<byte[]>("account"),
                row.GetValue<string>("token_kind"),
                row.GetValue<long>("token_id"),
                row.GetValue<int>("bucket"),
                row.GetValue<long>("round"),
                row.GetValue<int>("intra_round_order"),
                row.GetValue<int>("ordinal"),
                row.GetValue<string>("transaction_id"),
                row.IsNull("inner_path") ? null : row.GetValue<int[]>("inner_path"),
                row.GetValue<string>("effect_type"),
                row.GetValue<string>("delta"),
                row.GetValue<string>("balance_after"),
                row.IsNull("counterparty") ? null : row.GetValue<byte[]>("counterparty"),
                cancellationToken), cancellationToken).ConfigureAwait(false);

            // The bucket index: one row per account/token/bucket that received an effect (idempotent, and the partition's rows arrive together).
            var buckets = new List<(byte[] Account, string Kind, long Id, int Bucket)>();
            foreach (var row in rows)
            {
                var key = (row.GetValue<byte[]>("account"), row.GetValue<string>("token_kind"), row.GetValue<long>("token_id"), row.GetValue<int>("bucket"));
                if (lastBucket is not { } last || !last.Account.AsSpan().SequenceEqual(key.Item1) || last.Kind != key.Item2 || last.Id != key.Item3 || last.Bucket != key.Item4)
                {
                    buckets.Add(key);
                    lastBucket = key;
                }
            }

            await GatedAsync(
                copy.Gate,
                buckets,
                bucket => ExecuteAsync(
                    $"INSERT INTO {Table("effect_buckets_by_account_token")} (account, token_kind, token_id, bucket) VALUES (?, ?, ?, ?)",
                    cancellationToken,
                    bucket.Account,
                    bucket.Kind,
                    bucket.Id,
                    bucket.Bucket),
                cancellationToken).ConfigureAwait(false);
            copied += rows.Length;
        }

        copy.AddEffects(copied);
        return copied;
    }

    private async Task<long> CopyTokenEffectsAsync(ForkCopy copy, int range, CancellationToken cancellationToken)
    {
        long copied = 0;
        await foreach (var page in ScanAsync(copy.Source, "effects_by_token_bucket", "token_kind, token_id, bucket, shard", "token_kind, token_id, bucket, shard, round, intra_round_order, ordinal, account, transaction_id, effect_type, delta", range, cancellationToken).ConfigureAwait(false))
        {
            var rows = page.Where(row => row.GetValue<long>("round") <= copy.Round).ToArray();
            await GatedAsync(
                copy.Gate,
                rows,
                row => ExecuteAsync(
                    $"""
                    INSERT INTO {Table("effects_by_token_bucket")}
                      (token_kind, token_id, bucket, shard, round, intra_round_order, ordinal, account, transaction_id, effect_type, delta)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    cancellationToken,
                    row.GetValue<string>("token_kind"),
                    row.GetValue<long>("token_id"),
                    row.GetValue<int>("bucket"),
                    row.GetValue<short>("shard"),
                    row.GetValue<long>("round"),
                    row.GetValue<int>("intra_round_order"),
                    row.GetValue<int>("ordinal"),
                    row.GetValue<byte[]>("account"),
                    row.GetValue<string>("transaction_id"),
                    row.GetValue<string>("effect_type"),
                    row.GetValue<string>("delta")),
                cancellationToken).ConfigureAwait(false);
            copied += rows.Length;
        }

        return copied;
    }

    private async Task<long> CopyTouchedAsync(ForkCopy copy, int range, CancellationToken cancellationToken)
    {
        long copied = 0;
        await foreach (var page in ScanAsync(copy.Source, "accounts_touched_by_token", "token_kind, token_id, shard", "token_kind, token_id, shard, account, first_round", range, cancellationToken).ConfigureAwait(false))
        {
            var rows = page.Where(row => row.GetValue<long>("first_round") <= copy.Round).ToArray();
            await GatedAsync(
                copy.Gate,
                rows,
                row => ExecuteAsync(
                    $"INSERT INTO {Table("accounts_touched_by_token")} (token_kind, token_id, shard, account, first_round) VALUES (?, ?, ?, ?, ?)",
                    cancellationToken,
                    row.GetValue<string>("token_kind"),
                    row.GetValue<long>("token_id"),
                    row.GetValue<short>("shard"),
                    row.GetValue<byte[]>("account"),
                    row.GetValue<long>("first_round")),
                cancellationToken).ConfigureAwait(false);
            copied += rows.Length;
        }

        return copied;
    }

    /// <summary>
    /// The holdings are derived, never copied (the source holds the state of its tip): of every account/token of the target the newest copied
    /// effect is the first row of its newest bucket, and it carries the balance, the round and (by its type) the opt-in.
    /// </summary>
    private async Task<long> RebuildHoldingsAsync(ForkCopy copy, int range, CancellationToken cancellationToken)
    {
        long written = 0;
        (byte[] Account, string Kind, long Id)? lastPair = null;
        await foreach (var page in ScanAsync(_keyspace, "effect_buckets_by_account_token", "account, token_kind, token_id", "account, token_kind, token_id, bucket", range, cancellationToken).ConfigureAwait(false))
        {
            // The buckets of a pair are clustered newest first, so the first row seen of a pair names its newest bucket.
            var newest = new List<Row>();
            foreach (var row in page)
            {
                var account = row.GetValue<byte[]>("account");
                var kind = row.GetValue<string>("token_kind");
                var id = row.GetValue<long>("token_id");
                if (lastPair is { } last && last.Account.AsSpan().SequenceEqual(account) && last.Kind == kind && last.Id == id)
                {
                    continue;
                }

                lastPair = (account, kind, id);
                newest.Add(row);
            }

            await GatedAsync(
                copy.Gate,
                newest,
                async row =>
                {
                    var account = row.GetValue<byte[]>("account");
                    var kind = row.GetValue<string>("token_kind");
                    var id = row.GetValue<long>("token_id");
                    var effect = (await ExecuteAsync(
                        $"SELECT effect_type, balance_after, round FROM {Table("effects_by_account_token_bucket")} WHERE account = ? AND token_kind = ? AND token_id = ? AND bucket = ? LIMIT 1",
                        cancellationToken,
                        account,
                        kind,
                        id,
                        row.GetValue<int>("bucket")).ConfigureAwait(false)).FirstOrDefault();
                    if (effect is null)
                    {
                        return;
                    }

                    await UpsertHoldingAsync(
                        new Address(account),
                        kind,
                        id,
                        effect.GetValue<string>("balance_after"),
                        effect.GetValue<string>("effect_type") != EffectType.AssetOptOut.ToString(),
                        effect.GetValue<long>("round"),
                        cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            written += newest.Count;
        }

        copy.AddHoldings(written);
        return written;
    }

    private static async Task GatedAsync<T>(SemaphoreSlim gate, IReadOnlyCollection<T> items, Func<T, Task> work, CancellationToken cancellationToken) =>
        _ = await RunBoundedAsync<T, bool>(items, async item => { await work(item).ConfigureAwait(false); return true; }, cancellationToken, gate).ConfigureAwait(false);

    /// <summary>Token records: those created after the round are left out, a deletion after the round is undone. Other fields are taken as they are (ADR 0021).</summary>
    private async Task<long> CopyTokensAsync(ForkCopy copy, int range, CancellationToken cancellationToken)
    {
        string[] columns = ["token_kind", "token_id", "creator", "decimals", "symbol", "name", "standard", "standard_verified", "controlling_app_id", "created_round", "deleted_round", "metadata_json"];
        long copied = 0;
        await foreach (var page in ScanAsync(copy.Source, "token_by_id", "token_kind, token_id", string.Join(", ", columns), range, cancellationToken).ConfigureAwait(false))
        {
            var rows = page.Where(row => row.IsNull("created_round") || row.GetValue<long>("created_round") <= copy.Round).ToArray();
            await GatedAsync(
                copy.Gate,
                rows,
                row =>
                {
                    // Optional columns are left out instead of being bound to null (a null cell is a tombstone).
                    var present = columns
                        .Where(column => !row.IsNull(column) && !(column == "deleted_round" && row.GetValue<long>(column) > copy.Round))
                        .ToArray();
                    return ExecuteAsync(
                        $"INSERT INTO {Table("token_by_id")} ({string.Join(", ", present)}) VALUES ({string.Join(", ", present.Select(static _ => "?"))})",
                        cancellationToken,
                        [.. present.Select(column => row.GetValue<object>(column))]);
                },
                cancellationToken).ConfigureAwait(false);
            copied += rows.Length;
        }

        return copied;
    }

    /// <summary>The build records up to the round (#182). Written conditionally only, as everywhere in that table.</summary>
    private async Task CopyIndexingTermsAsync(ForkCopy copy, CancellationToken cancellationToken)
    {
        var rows = (await ExecuteAsync($"SELECT from_round, version, started_at FROM {copy.Source}.indexing_term WHERE chain = ?", cancellationToken, _chain).ConfigureAwait(false))
            .Where(row => row.GetValue<long>("from_round") <= copy.Round)
            .ToArray();
        foreach (var row in rows)
        {
            var insert = await BindAsync(
                $"INSERT INTO {Table("indexing_term")} (chain, from_round, version, started_at) VALUES (?, ?, ?, ?) IF NOT EXISTS",
                cancellationToken,
                _chain,
                row.GetValue<long>("from_round"),
                row.GetValue<string>("version"),
                row.GetValue<DateTimeOffset>("started_at")).ConfigureAwait(false);
            await ExecuteConditionalAsync(insert, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pages of one token range of a table of <paramref name="keyspace"/>, read at LOCAL_QUORUM. The query bounds the scan by token only (a
    /// maintenance scan: no ALLOW FILTERING), the caller filters by round. The ring is split into <see cref="ForkRanges"/> ranges; the first
    /// includes the smallest token.
    /// </summary>
    private async IAsyncEnumerable<Row[]> ScanAsync(string keyspace, string table, string partitionKey, string columns, int range, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const long step = 1L << 56;
        var low = long.MinValue + (range * step);
        var high = range == ForkRanges - 1 ? long.MaxValue : long.MinValue + ((range + 1) * step);
        var statement = await BindAsync(
            $"SELECT {columns} FROM {keyspace}.{table} WHERE token({partitionKey}) {(range == 0 ? ">=" : ">")} ? AND token({partitionKey}) <= ?",
            cancellationToken,
            low,
            high).ConfigureAwait(false);
        statement.SetPageSize(ForkPageSize);
        statement.SetConsistencyLevel(ConsistencyLevel.LocalQuorum);

        // One page per request (no automatic paging, which would block while enumerating): the paging state continues the scan.
        statement.SetAutoPage(false);
        while (true)
        {
            var rows = await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false);
            Row[] page = [.. rows];
            if (page.Length > 0)
            {
                yield return page;
            }

            if (rows.PagingState is not { Length: > 0 } next)
            {
                yield break;
            }

            statement.SetPagingState(next);
        }
    }

    /// <summary>State of one copy: its request, the shared bound of writes in flight, and the counters for the progress reports.</summary>
    private sealed class ForkCopy(string source, long round, int writeConcurrency, IProgress<PartialRebuildProgress>? progress) : IDisposable
    {
        private long _effects;
        private long _holdings;

        public string Source { get; } = source;

        public long Round { get; } = round;

        public SemaphoreSlim Gate { get; } = new(writeConcurrency);

        public long Effects => Interlocked.Read(ref _effects);

        public long Holdings => Interlocked.Read(ref _holdings);

        public void Dispose() => Gate.Dispose();

        public void AddEffects(long count) => Interlocked.Add(ref _effects, count);

        public void AddHoldings(long count) => Interlocked.Add(ref _holdings, count);

        public void Report(string stage, long rows, int rangesDone, int rangesTotal) => progress?.Report(new PartialRebuildProgress(stage, rows, rangesDone, rangesTotal));
    }
}
