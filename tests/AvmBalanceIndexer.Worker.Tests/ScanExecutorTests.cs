using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.Worker.Tests;

public class ScanExecutorTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<ScanStorage> IndexedAsync()
    {
        var storage = new ScanStorage();
        var indexer = new RoundIndexer(
            Adapter(),
            new AlgoEffectNormalizer(),
            storage,
            Options.Create(new IndexerOptions { FetchConcurrency = 1, BalanceCacheEntries = 1_000 }),
            NullLogger<RoundIndexer>.Instance);
        await indexer.RunAsync(LastRound, CancellationToken.None);
        return storage;
    }

    private static ScanExecutor Executor(ScanStorage storage, int pageSize = 2, int retries = 3)
    {
        var options = Options.Create(new ScanOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(1),
            StaleAfter = TimeSpan.FromMinutes(2),
            PageSize = pageSize,
            MaxStepRetries = retries,
        });
        return new ScanExecutor(storage, storage, storage, options, new FixedClock(Now), NullLogger<ScanExecutor>.Instance);
    }

    private static ScanJob NewJob(ulong round = 4, bool includeZero = true, TimeSpan? created = null) =>
        new(Guid.NewGuid(), TokenKey.Native, round, includeZero, ScanState.Pending, 0, null, null, Now + (created ?? TimeSpan.Zero), Now + (created ?? TimeSpan.Zero), Now.AddHours(24));

    private static async Task<ScanJob> WaitForAsync(IScanJobStore store, Guid id, Func<ScanJob, bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var job = await store.GetJobAsync(id, timeout.Token);
            if (job is not null && done(job))
            {
                return job;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<List<Holding>> ReadAllAsync(IScanJobStore store, Guid id)
    {
        var all = new List<Holding>();
        string? cursor = null;
        do
        {
            var page = await store.GetResultAsync(id, cursor, 3, CancellationToken.None);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return all;
    }

    [Fact]
    public async Task A_Scan_Equals_The_Synchronous_Holder_List_At_The_Same_Round()
    {
        var storage = await IndexedAsync();
        var job = NewJob();
        await storage.CreateAsync(job, CancellationToken.None);
        using var executor = Executor(storage);

        await executor.StartAsync(CancellationToken.None);
        var finished = await WaitForAsync(storage, job.Id, static j => j.State == ScanState.Completed);
        await executor.StopAsync(CancellationToken.None);

        var expected = await storage.GetHoldersAtRoundAsync(TokenKey.Native, 4, true, null, 100, CancellationToken.None);
        var scanned = await ReadAllAsync(storage, job.Id);
        Assert.Equal(expected.Items.Count, finished.HoldersFound);
        Assert.Equal(expected.Items, scanned);
        Assert.True(storage.AppendCalls >= 3, "The scan should have worked in several steps.");
    }

    [Fact]
    public async Task A_Cancelled_Scan_Stops_After_The_Step_It_Is_In()
    {
        var storage = await IndexedAsync();
        var job = NewJob();
        await storage.CreateAsync(job, CancellationToken.None);
        storage.OnAppend = () => storage.CancelAsync(job.Id, Now, CancellationToken.None).GetAwaiter().GetResult();
        using var executor = Executor(storage, pageSize: 1);

        await executor.StartAsync(CancellationToken.None);
        await WaitForAsync(storage, job.Id, static j => j.State == ScanState.Cancelled);
        await Task.Delay(100);
        await executor.StopAsync(CancellationToken.None);

        Assert.Equal(ScanState.Cancelled, (await storage.GetJobAsync(job.Id, CancellationToken.None))!.State);
        Assert.Equal(2, storage.AppendCalls);
    }

    [Fact]
    public async Task A_Scan_Abandoned_By_Its_Owner_Is_Resumed_From_The_Checkpoint()
    {
        var storage = await IndexedAsync();
        var job = NewJob();
        await storage.CreateAsync(job, CancellationToken.None);

        // Another replica did the first step and then died long ago.
        var firstPage = await storage.GetHoldersAtRoundAsync(TokenKey.Native, 4, true, null, 2, CancellationToken.None);
        var claimed = await storage.ClaimNextAsync("dead-replica", Now.AddMinutes(-30), Now.AddHours(-1), CancellationToken.None);
        await storage.AppendAsync(claimed!.Id, "dead-replica", firstPage.Items, firstPage.NextCursor, Now.AddMinutes(-30), CancellationToken.None);
        using var executor = Executor(storage);

        await executor.StartAsync(CancellationToken.None);
        var finished = await WaitForAsync(storage, job.Id, static j => j.State == ScanState.Completed);
        await executor.StopAsync(CancellationToken.None);

        var expected = await storage.GetHoldersAtRoundAsync(TokenKey.Native, 4, true, null, 100, CancellationToken.None);
        Assert.Equal(expected.Items, await ReadAllAsync(storage, job.Id));
        Assert.Equal(expected.Items.Count, finished.HoldersFound);
    }

    [Fact]
    public async Task Transient_Read_Failures_Are_Retried_And_Persistent_Ones_Fail_The_Scan_With_A_Safe_Message()
    {
        var storage = await IndexedAsync();
        var flaky = NewJob();
        await storage.CreateAsync(flaky, CancellationToken.None);
        storage.FailReads = 2;
        using (var executor = Executor(storage, retries: 3))
        {
            await executor.StartAsync(CancellationToken.None);
            await WaitForAsync(storage, flaky.Id, static j => j.State == ScanState.Completed);
            await executor.StopAsync(CancellationToken.None);
        }

        var doomed = NewJob(created: TimeSpan.FromSeconds(1));
        await storage.CreateAsync(doomed, CancellationToken.None);
        storage.FailReads = int.MaxValue;
        using (var executor = Executor(storage, retries: 2))
        {
            await executor.StartAsync(CancellationToken.None);
            var failed = await WaitForAsync(storage, doomed.Id, static j => j.State == ScanState.Failed);
            await executor.StopAsync(CancellationToken.None);

            Assert.Contains("timing out", failed.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("simulated", failed.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_Scan_Whose_Last_Page_Was_Stored_But_Never_Completed_Is_Completed_Without_Repeating_Rows()
    {
        var storage = await IndexedAsync();
        var job = NewJob();
        await storage.CreateAsync(job, CancellationToken.None);
        var all = await storage.GetHoldersAtRoundAsync(TokenKey.Native, 4, true, null, 100, CancellationToken.None);
        await storage.ClaimNextAsync("dead-replica", Now.AddMinutes(-30), Now.AddHours(-1), CancellationToken.None);
        await storage.AppendAsync(job.Id, "dead-replica", all.Items, ScanJob.EndOfList, Now.AddMinutes(-30), CancellationToken.None);
        using var executor = Executor(storage);

        await executor.StartAsync(CancellationToken.None);
        await WaitForAsync(storage, job.Id, static j => j.State == ScanState.Completed);
        await executor.StopAsync(CancellationToken.None);

        Assert.Equal(all.Items, await ReadAllAsync(storage, job.Id));
    }

    [Fact]
    public async Task A_Step_Whose_Outcome_Was_Lost_Is_Not_Repeated()
    {
        var storage = await IndexedAsync();
        var job = NewJob();
        await storage.CreateAsync(job, CancellationToken.None);
        storage.LoseResponseOfAppend = 2;
        using var executor = Executor(storage, pageSize: 2, retries: 3);

        await executor.StartAsync(CancellationToken.None);
        await WaitForAsync(storage, job.Id, static j => j.State == ScanState.Completed);
        await executor.StopAsync(CancellationToken.None);

        var expected = await storage.GetHoldersAtRoundAsync(TokenKey.Native, 4, true, null, 100, CancellationToken.None);
        Assert.Equal(expected.Items, await ReadAllAsync(storage, job.Id));
    }

    [Fact]
    public async Task A_Scan_Queue_That_Is_Not_Ready_Does_Not_Stop_The_Executor()
    {
        var storage = await IndexedAsync();
        storage.FailClaims = 3;
        var job = NewJob();
        await storage.CreateAsync(job, CancellationToken.None);
        using var executor = Executor(storage);

        await executor.StartAsync(CancellationToken.None);
        await WaitForAsync(storage, job.Id, static j => j.State == ScanState.Completed);
        await executor.StopAsync(CancellationToken.None);

        Assert.True(storage.FailClaims <= 0);
    }

    [Fact]
    public async Task Idle_Executors_Remove_Expired_Scans()
    {
        var storage = await IndexedAsync();
        var expired = NewJob() with { ExpiresAt = Now.AddMinutes(-1), State = ScanState.Completed };
        await storage.CreateAsync(expired, CancellationToken.None);
        using var executor = Executor(storage);

        await executor.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await storage.GetJobAsync(expired.Id, timeout.Token) is not null)
        {
            await Task.Delay(10, timeout.Token);
        }

        await executor.StopAsync(CancellationToken.None);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The reference storage with hooks to cancel a scan between steps and to make holder reads fail.</summary>
    private sealed class ScanStorage : InMemoryChainStorage
    {
        private int _appendCalls;
        private int _failReads;

        public ScanStorage()
            : base(new ChainContext(ChainId.AlgorandTestnet))
        {
        }

        public int AppendCalls => Volatile.Read(ref _appendCalls);

        public Action? OnAppend { get; set; }

        /// <summary>The n-th append is applied but reported to the caller as a failed request (the response was lost).</summary>
        public int LoseResponseOfAppend { get; set; }

        public int FailClaims { get; set; }

        public override Task<ScanJob?> ClaimNextAsync(string owner, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken cancellationToken)
        {
            if (FailClaims-- > 0)
            {
                // Not a classified transient error: a missing table or a bug must not take the process down either.
                return Task.FromException<ScanJob?>(new InvalidOperationException("the scan tables do not exist yet"));
            }

            return base.ClaimNextAsync(owner, now, staleBefore, cancellationToken);
        }

        public int FailReads
        {
            get => Volatile.Read(ref _failReads);
            set => Volatile.Write(ref _failReads, value);
        }

        public override Task<Page<Holding>> GetHoldersAtRoundAsync(TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken)
        {
            if (FailReads > 0)
            {
                Interlocked.Decrement(ref _failReads);
                return Task.FromException<Page<Holding>>(new StorageTimeoutException("simulated slow storage"));
            }

            return base.GetHoldersAtRoundAsync(token, round, includeZero, cursor, limit, cancellationToken);
        }

        public override async Task<bool> AppendAsync(Guid id, string owner, IReadOnlyList<Holding> items, string? nextCursor, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var applied = await base.AppendAsync(id, owner, items, nextCursor, now, cancellationToken);
            var call = Interlocked.Increment(ref _appendCalls);
            OnAppend?.Invoke();
            if (call == LoseResponseOfAppend)
            {
                throw new IOException("the response was lost");
            }

            return applied;
        }
    }
}
