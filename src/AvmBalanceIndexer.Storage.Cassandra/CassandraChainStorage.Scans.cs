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
        // The index entries come first and are plain writes: a crash after them leaves an entry without a job (cleaned up later), never a
        // job nobody can find. Every write to the job row itself is a lightweight transaction, creation and deletion included: Paxos
        // writes carry server-side timestamps, and mixing them with client-timestamped writes lets a plain write silently shadow a
        // conditional one.
        await ExecuteAsync(
            $"INSERT INTO {Table("scan_job_index")} (chain, id, created_at) VALUES (?, ?, ?)",
            cancellationToken,
            _chain,
            job.Id,
            job.CreatedAt).ConfigureAwait(false);
        await ExecuteAsync(
            $"INSERT INTO {Table("scan_active")} (chain, id, created_at) VALUES (?, ?, ?)",
            cancellationToken,
            _chain,
            job.Id,
            job.CreatedAt).ConfigureAwait(false);
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
    }

    public async Task<ScanJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        (await ReadJobAsync(id, cancellationToken).ConfigureAwait(false))?.Job;

    public async Task<int> CountActiveAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await ReadActiveJobsAsync(cancellationToken).ConfigureAwait(false);
        return jobs.Count(j => !j.Job.IsFinished && j.Job.ExpiresAt > now);
    }

    public async Task<ScanJob?> ClaimNextAsync(string owner, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var candidates = (await ReadActiveJobsAsync(cancellationToken).ConfigureAwait(false))
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
        if (await ExecuteConditionalAsync(statement, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        // Not the owner any more (cancelled, expired, taken over): the rows just written belong to nobody. Another owner may have
        // written the same keys already, so only take back what lies beyond the stored count.
        if (await ReadJobAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            await RunBoundedAsync(
                rows,
                row => ExecuteAsync(
                    $"DELETE FROM {Table("scan_result")} WHERE job_id = ? AND bucket = ? AND seq = ?",
                    cancellationToken,
                    id,
                    checked((int)(row.Seq / ScanBucketSize)),
                    row.Seq),
                cancellationToken).ConfigureAwait(false);
        }

        return false;
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
                await ForgetActiveAsync(id, cancellationToken).ConfigureAwait(false);
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

        if (after >= current.Job.HoldersFound - 1)
        {
            // Nothing past this position yet (or a position far beyond anything stored): a reader following a running scan keeps its cursor.
            return new Page<Holding>([], after >= 0 && !current.Job.IsFinished ? after.ToString(CultureInfo.InvariantCulture) : null, current.Job.Round);
        }

        var items = new List<Holding>();
        var seqs = new List<long>();
        for (var bucket = (int)((after + 1) / ScanBucketSize); (long)bucket * ScanBucketSize < current.Job.HoldersFound && items.Count <= pageSize; bucket++)
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

        var delivered = items.Count > 0 ? seqs[items.Count - 1] : after;
        var next = delivered >= 0 && (hasMore || !current.Job.IsFinished) ? delivered.ToString(CultureInfo.InvariantCulture) : null;
        return new Page<Holding>(items, next, current.Job.Round);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var removed = 0;
        var indexed = await ExecuteAsync($"SELECT id, created_at FROM {Table("scan_job_index")} WHERE chain = ?", cancellationToken, _chain).ConfigureAwait(false);
        foreach (var entry in indexed.Select(static row => (Id: row.GetValue<Guid>("id"), CreatedAt: row.GetValue<DateTimeOffset>("created_at"))).ToArray())
        {
            var current = await ReadJobAsync(entry.Id, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                // The index entry is written before the job; give a creation in progress time to finish before calling it an orphan.
                if (now - entry.CreatedAt > OrphanGrace)
                {
                    await ForgetAsync(entry.Id, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (current.Value.Job.ExpiresAt > now)
            {
                continue;
            }

            // Results first (every bucket that still holds rows, which also catches rows a displaced owner wrote past its checkpoint),
            // then the job, then the markers: a crash in between leaves something the next run finishes.
            for (var bucket = 0; ; bucket++)
            {
                var any = (await ExecuteAsync($"SELECT seq FROM {Table("scan_result")} WHERE job_id = ? AND bucket = ? LIMIT 1", cancellationToken, entry.Id, bucket).ConfigureAwait(false)).Any();
                if (!any && (long)bucket * ScanBucketSize >= current.Value.Job.HoldersFound)
                {
                    break;
                }

                if (any)
                {
                    await ExecuteAsync($"DELETE FROM {Table("scan_result")} WHERE job_id = ? AND bucket = ?", cancellationToken, entry.Id, bucket).ConfigureAwait(false);
                }
            }

            var delete = await BindAsync($"DELETE FROM {Table("scan_job")} WHERE id = ? IF EXISTS", cancellationToken, entry.Id).ConfigureAwait(false);
            await ExecuteConditionalAsync(delete, cancellationToken).ConfigureAwait(false);
            await ForgetAsync(entry.Id, cancellationToken).ConfigureAwait(false);
            removed++;
        }

        return removed;
    }

    private static readonly TimeSpan OrphanGrace = TimeSpan.FromMinutes(10);

    private async Task ForgetAsync(Guid id, CancellationToken cancellationToken)
    {
        await ExecuteAsync($"DELETE FROM {Table("scan_job_index")} WHERE chain = ? AND id = ?", cancellationToken, _chain, id).ConfigureAwait(false);
        await ForgetActiveAsync(id, cancellationToken).ConfigureAwait(false);
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
        var applied = await ExecuteConditionalAsync(statement, cancellationToken).ConfigureAwait(false);
        if (applied)
        {
            await ForgetActiveAsync(id, cancellationToken).ConfigureAwait(false);
        }

        return applied;
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

    /// <summary>
    /// The jobs that are pending or running, from a small marker table that only contains them (finished and expired jobs are not read
    /// on every poll). A marker whose job already finished is removed when it is met.
    /// </summary>
    private async Task<List<(ScanJob Job, string? Owner)>> ReadActiveJobsAsync(CancellationToken cancellationToken)
    {
        var markers = (await ExecuteAsync($"SELECT id, created_at FROM {Table("scan_active")} WHERE chain = ?", cancellationToken, _chain).ConfigureAwait(false))
            .Select(static row => (Id: row.GetValue<Guid>("id"), CreatedAt: row.GetValue<DateTimeOffset>("created_at")))
            .ToArray();
        var jobs = await RunBoundedAsync(markers, marker => ReadJobAsync(marker.Id, cancellationToken), cancellationToken).ConfigureAwait(false);
        var active = new List<(ScanJob Job, string? Owner)>(jobs.Length);
        for (var i = 0; i < jobs.Length; i++)
        {
            if (jobs[i] is { } job && !job.Job.IsFinished)
            {
                active.Add(job);
            }
            else if (jobs[i] is not null || DateTimeOffset.UtcNow - markers[i].CreatedAt > OrphanGrace)
            {
                // Finished jobs lose their marker. A marker without a job may belong to a creation that is still in progress (the marker
                // is written first), so it is only removed once it is too old for that.
                await ForgetActiveAsync(markers[i].Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return active;
    }

    private Task<RowSet> ForgetActiveAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync($"DELETE FROM {Table("scan_active")} WHERE chain = ? AND id = ?", cancellationToken, _chain, id);

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
