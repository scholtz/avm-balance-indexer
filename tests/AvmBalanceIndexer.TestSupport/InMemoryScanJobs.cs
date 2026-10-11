using System.Globalization;
using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.TestSupport;

public partial class InMemoryChainStorage : IScanJobStore
{
    private readonly object _scanGate = new();
    private readonly Dictionary<Guid, (ScanJob Job, string? Owner)> _scanJobs = [];
    private readonly Dictionary<Guid, List<Holding>> _scanResults = [];

    public Task CreateAsync(ScanJob job, CancellationToken cancellationToken)
    {
        lock (_scanGate)
        {
            _scanJobs.Add(job.Id, (job, null));
            _scanResults[job.Id] = [];
        }

        return Task.CompletedTask;
    }

    public Task<ScanJob?> GetJobAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_scanGate)
        {
            return Task.FromResult(_scanJobs.TryGetValue(id, out var entry) ? entry.Job : null);
        }
    }

    public Task<int> CountActiveAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_scanGate)
        {
            return Task.FromResult(_scanJobs.Values.Count(e => !e.Job.IsFinished && e.Job.ExpiresAt > now));
        }
    }

    public virtual Task<ScanJob?> ClaimNextAsync(string owner, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        lock (_scanGate)
        {
            var next = _scanJobs.Values
                .Where(e => e.Job.ExpiresAt > now && (e.Job.State == ScanState.Pending || (e.Job.State == ScanState.Running && e.Job.UpdatedAt < staleBefore)))
                .OrderBy(static e => e.Job.CreatedAt)
                .Select(static e => (ScanJob?)e.Job)
                .FirstOrDefault();
            if (next is null)
            {
                return Task.FromResult<ScanJob?>(null);
            }

            var claimed = next with { State = ScanState.Running, UpdatedAt = now };
            _scanJobs[next.Id] = (claimed, owner);
            return Task.FromResult<ScanJob?>(claimed);
        }
    }

    public virtual Task<bool> AppendAsync(Guid id, string owner, IReadOnlyList<Holding> items, string? nextCursor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_scanGate)
        {
            if (!Owns(id, owner, out var job))
            {
                return Task.FromResult(false);
            }

            _scanResults[id].AddRange(items);
            _scanJobs[id] = (job with { HoldersFound = _scanResults[id].Count, Cursor = nextCursor, UpdatedAt = now }, owner);
            return Task.FromResult(true);
        }
    }

    public Task<bool> CompleteAsync(Guid id, string owner, DateTimeOffset now, CancellationToken cancellationToken) =>
        Finish(id, owner, ScanState.Completed, null, now);

    public Task<bool> FailAsync(Guid id, string owner, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        Finish(id, owner, ScanState.Failed, reason, now);

    public virtual Task<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_scanGate)
        {
            if (!_scanJobs.TryGetValue(id, out var entry) || entry.Job.IsFinished)
            {
                return Task.FromResult(false);
            }

            _scanJobs[id] = (entry.Job with { State = ScanState.Cancelled, UpdatedAt = now }, entry.Owner);
            return Task.FromResult(true);
        }
    }

    public Task<Page<Holding>> GetResultAsync(Guid id, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var pageSize = limit < 1 ? 1 : Math.Min(limit, 1000);
        long after = -1;
        if (cursor is not null && !long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out after))
        {
            throw new ArgumentException("Invalid cursor.", nameof(cursor));
        }

        lock (_scanGate)
        {
            if (!_scanJobs.TryGetValue(id, out var entry))
            {
                return Task.FromResult(new Page<Holding>([], null, 0));
            }

            var rows = _scanResults[id];
            if (after >= rows.Count - 1)
            {
                return Task.FromResult(new Page<Holding>([], after >= 0 && !entry.Job.IsFinished ? after.ToString(CultureInfo.InvariantCulture) : null, entry.Job.Round));
            }

            var skip = (int)(after + 1);
            var items = rows.Skip(skip).Take(pageSize).ToList();
            var delivered = skip + items.Count - 1;
            var next = delivered >= 0 && (skip + items.Count < rows.Count || !entry.Job.IsFinished) ? delivered.ToString(CultureInfo.InvariantCulture) : null;
            return Task.FromResult(new Page<Holding>(items, next, entry.Job.Round));
        }
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_scanGate)
        {
            var expired = _scanJobs.Where(kv => kv.Value.Job.ExpiresAt <= now).Select(static kv => kv.Key).ToList();
            foreach (var id in expired)
            {
                _scanJobs.Remove(id);
                _scanResults.Remove(id);
            }

            return Task.FromResult(expired.Count);
        }
    }

    private Task<bool> Finish(Guid id, string owner, ScanState state, string? error, DateTimeOffset now)
    {
        lock (_scanGate)
        {
            if (!Owns(id, owner, out var job))
            {
                return Task.FromResult(false);
            }

            _scanJobs[id] = (job with { State = state, Error = error, UpdatedAt = now }, owner);
            return Task.FromResult(true);
        }
    }

    private bool Owns(Guid id, string owner, out ScanJob job)
    {
        if (_scanJobs.TryGetValue(id, out var entry) && entry.Job.State == ScanState.Running && entry.Owner == owner)
        {
            job = entry.Job;
            return true;
        }

        job = null!;
        return false;
    }
}
