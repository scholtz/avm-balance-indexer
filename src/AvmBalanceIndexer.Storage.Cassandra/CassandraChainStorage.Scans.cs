using System.Globalization;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage : IScanJobStore
{
    /// <summary>Result rows per partition; bounds partition size and lets a page read touch one or two partitions.</summary>
    private const int ScanBucketSize = 1_000;

    private const string ScanJobColumns = "id, token_kind, token_id, round, include_zero, state, holders_found, cursor, error, created_at, updated_at, expires_at, owner";

    public async Task CreateAsync(ScanJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        // Every write to a job row is a lightweight transaction, creation and deletion included: Paxos writes carry server-side
        // timestamps, and mixing them with client-timestamped writes lets a plain write silently shadow a conditional one.
        var create = await BindAsync(
            $"""
            INSERT INTO {Table("scan_job")} (id, token_kind, token_id, round, include_zero, state, holders_found, created_at, updated_at, expires_at)
            VALUES (?, ?, ?, ?, ?, ?, 0, ?, ?, ?) IF NOT EXISTS
            """,
            cancellationToken,
            job.Id,
            job.Token.Kind.ToStorageName(),
            StorageId(job.Token.TokenId),
            unchecked((long)job.Round),
            job.IncludeZero,
            job.State.ToString(),
            job.CreatedAt,
            job.UpdatedAt,
            job.ExpiresAt).ConfigureAwait(false);
        if (!await ExecuteConditionalAsync(create, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Scan job {job.Id} already exists.");
        }

        await ExecuteAsync(
            $"INSERT INTO {Table("scan_job_index")} (chain, id, created_at) VALUES (?, ?, ?)",
            cancellationToken,
            _chain,
            job.Id,
            job.CreatedAt).ConfigureAwait(false);
    }

    public async Task<ScanJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        (await ReadJobAsync(id, cancellationToken).ConfigureAwait(false))?.Job;

    public async Task<int> CountActiveAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await ReadAllJobsAsync(cancellationToken).ConfigureAwait(false);
        return jobs.Count(j => !j.Job.IsFinished && j.Job.ExpiresAt > now);
    }

    public async Task<ScanJob?> ClaimNextAsync(string owner, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var candidates = (await ReadAllJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(j => j.Job.ExpiresAt > now && (j.Job.State == ScanState.Pending || (j.Job.State == ScanState.Running && j.Job.UpdatedAt < staleBefore)))
            .OrderBy(static j => j.Job.CreatedAt);
        foreach (var (job, _) in candidates)
        {
            // Conditional on the state and timestamp that were read, so only one replica wins a job.
            var statement = await BindAsync(
                $"UPDATE {Table("scan_job")} SET state = 'Running', owner = ?, updated_at = ? WHERE id = ? IF state = ? AND updated_at = ?",
                cancellationToken,
                owner,
                now,
                job.Id,
                job.State.ToString(),
                job.UpdatedAt).ConfigureAwait(false);
            if (await ExecuteConditionalAsync(statement, cancellationToken).ConfigureAwait(false))
            {
                return job with { State = ScanState.Running, UpdatedAt = now };
            }
        }

        return null;
    }

    public async Task<bool> AppendAsync(Guid id, string owner, IReadOnlyList<Holding> items, string? nextCursor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (await ReadJobAsync(id, cancellationToken).ConfigureAwait(false) is not { Job.State: ScanState.Running } current || current.Owner != owner)
        {
            return false;
        }

        // Rows are numbered from the stored count, so a retry after a crash rewrites the same keys with the same values; the
        // conditional update below is what makes them count.
        var first = current.Job.HoldersFound;
        var insert = $"INSERT INTO {Table("scan_result")} (job_id, bucket, seq, account, amount, opted_in, last_changed_round) VALUES (?, ?, ?, ?, ?, ?, ?)";
        var rows = items.Select((item, index) => (Item: item, Seq: first + index)).ToArray();
        await RunBoundedAsync(
            rows,
            row => ExecuteAsync(
                insert,
                cancellationToken,
                id,
                checked((int)(row.Seq / ScanBucketSize)),
                row.Seq,
                row.Item.Account.ToArray(),
                row.Item.Amount.ToString(),
                row.Item.OptedIn,
                unchecked((long)row.Item.LastChangedRound)),
            cancellationToken).ConfigureAwait(false);

        var statement = await BindAsync(
            $"UPDATE {Table("scan_job")} SET holders_found = ?, cursor = ?, updated_at = ? WHERE id = ? IF owner = ? AND state = 'Running' AND holders_found = ?",
            cancellationToken,
            first + items.Count,
            nextCursor,
            now,
            id,
            owner,
            first).ConfigureAwait(false);
        return await ExecuteConditionalAsync(statement, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CompleteAsync(Guid id, string owner, DateTimeOffset now, CancellationToken cancellationToken) =>
        FinishAsync(id, owner, ScanState.Completed, null, now, cancellationToken);

    public Task<bool> FailAsync(Guid id, string owner, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        FinishAsync(id, owner, ScanState.Failed, reason, now, cancellationToken);

    public async Task<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A concurrent claim or checkpoint can change the state between the read and the update; try again on the new state.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (await ReadJobAsync(id, cancellationToken).ConfigureAwait(false) is not { } current || current.Job.IsFinished)
            {
                return false;
            }

            var statement = await BindAsync(
                $"UPDATE {Table("scan_job")} SET state = 'Cancelled', updated_at = ? WHERE id = ? IF state = ?",
                cancellationToken,
                now,
                id,
                current.Job.State.ToString()).ConfigureAwait(false);
            if (await ExecuteConditionalAsync(statement, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<Page<Holding>> GetResultAsync(Guid id, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = ClampLimit(limit);
        long after = -1;
        if (cursor is not null && !long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out after))
        {
            throw new ArgumentException("Invalid cursor.", nameof(cursor));
        }

        if (await ReadJobAsync(id, cancellationToken).ConfigureAwait(false) is not { } current)
        {
            return new Page<Holding>([], null, 0);
        }

        var items = new List<Holding>();
        var seqs = new List<long>();
        for (var bucket = checked((int)((after + 1) / ScanBucketSize)); (long)bucket * ScanBucketSize < current.Job.HoldersFound && items.Count <= pageSize; bucket++)
        {
            var rows = await ExecuteAsync(
                $"SELECT seq, account, amount, opted_in, last_changed_round FROM {Table("scan_result")} WHERE job_id = ? AND bucket = ? AND seq > ? LIMIT ?",
                cancellationToken,
                id,
                bucket,
                after,
                pageSize + 1 - items.Count).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var seq = row.GetValue<long>("seq");
                if (seq >= current.Job.HoldersFound)
                {
                    break;
                }

                items.Add(new Holding(
                    new Address(row.GetValue<byte[]>("account")),
                    current.Job.Token,
                    Amount.Parse(row.GetValue<string>("amount")),
                    row.GetValue<bool>("opted_in"),
                    (ulong)row.GetValue<long>("last_changed_round")));
                seqs.Add(seq);
            }
        }

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new Page<Holding>(items, hasMore ? seqs[items.Count - 1].ToString(CultureInfo.InvariantCulture) : null, current.Job.Round);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var removed = 0;
        var indexed = await ExecuteAsync($"SELECT id FROM {Table("scan_job_index")} WHERE chain = ?", cancellationToken, _chain).ConfigureAwait(false);
        foreach (var id in indexed.Select(static row => row.GetValue<Guid>("id")).ToArray())
        {
            var current = await ReadJobAsync(id, cancellationToken).ConfigureAwait(false);
            if (current is { } live && live.Job.ExpiresAt > now)
            {
                continue;
            }

            // Results first, then the job, then the index entry: a crash in between leaves something the next run finishes.
            if (current is { } expired)
            {
                var buckets = (expired.Job.HoldersFound + ScanBucketSize - 1) / ScanBucketSize;
                for (var bucket = 0; bucket < buckets; bucket++)
                {
                    await ExecuteAsync($"DELETE FROM {Table("scan_result")} WHERE job_id = ? AND bucket = ?", cancellationToken, id, bucket).ConfigureAwait(false);
                }

                var delete = await BindAsync($"DELETE FROM {Table("scan_job")} WHERE id = ? IF EXISTS", cancellationToken, id).ConfigureAwait(false);
                await ExecuteConditionalAsync(delete, cancellationToken).ConfigureAwait(false);
                removed++;
            }

            await ExecuteAsync($"DELETE FROM {Table("scan_job_index")} WHERE chain = ? AND id = ?", cancellationToken, _chain, id).ConfigureAwait(false);
        }

        return removed;
    }

    private async Task<bool> FinishAsync(Guid id, string owner, ScanState state, string? error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var statement = await BindAsync(
            $"UPDATE {Table("scan_job")} SET state = ?, error = ?, updated_at = ? WHERE id = ? IF owner = ? AND state = 'Running'",
            cancellationToken,
            state.ToString(),
            error,
            now,
            id,
            owner).ConfigureAwait(false);
        return await ExecuteConditionalAsync(statement, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ExecuteConditionalAsync(BoundStatement statement, CancellationToken cancellationToken)
    {
        statement.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
        return (await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false)).First().GetValue<bool>("[applied]");
    }

    private async Task<(ScanJob Job, string? Owner)?> ReadJobAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await ExecuteAsync($"SELECT {ScanJobColumns} FROM {Table("scan_job")} WHERE id = ?", cancellationToken, id).ConfigureAwait(false);
        return rows.FirstOrDefault() is { } row ? MapJob(row) : null;
    }

    private async Task<List<(ScanJob Job, string? Owner)>> ReadAllJobsAsync(CancellationToken cancellationToken)
    {
        var ids = (await ExecuteAsync($"SELECT id FROM {Table("scan_job_index")} WHERE chain = ?", cancellationToken, _chain).ConfigureAwait(false))
            .Select(static row => row.GetValue<Guid>("id"))
            .ToArray();
        var jobs = await RunBoundedAsync(ids, id => ReadJobAsync(id, cancellationToken), cancellationToken).ConfigureAwait(false);
        return [.. jobs.Where(static j => j is not null).Select(static j => j!.Value)];
    }

    private static TokenKind ParseKind(string stored) =>
        TokenKindExtensions.TryParseStorageName(stored, out var kind) ? kind : throw new InvalidDataException($"The database holds an unknown token kind \"{stored}\".");

    private static (ScanJob Job, string? Owner) MapJob(Row row)
    {
        var job = new ScanJob(
            row.GetValue<Guid>("id"),
            new TokenKey(ParseKind(row.GetValue<string>("token_kind")), (ulong)row.GetValue<long>("token_id")),
            (ulong)row.GetValue<long>("round"),
            row.GetValue<bool>("include_zero"),
            Enum.Parse<ScanState>(row.GetValue<string>("state")),
            row.IsNull("holders_found") ? 0 : row.GetValue<long>("holders_found"),
            row.IsNull("cursor") ? null : row.GetValue<string>("cursor"),
            row.IsNull("error") ? null : row.GetValue<string>("error"),
            row.GetValue<DateTimeOffset>("created_at"),
            row.GetValue<DateTimeOffset>("updated_at"),
            row.GetValue<DateTimeOffset>("expires_at"));
        return (job, row.IsNull("owner") ? null : row.GetValue<string>("owner"));
    }
}
