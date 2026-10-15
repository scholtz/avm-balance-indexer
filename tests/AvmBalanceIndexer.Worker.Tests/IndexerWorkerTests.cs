using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker.Tests;

public class IndexerWorkerTests : IDisposable
{
    private readonly int _previousExitCode = Environment.ExitCode;

    public void Dispose()
    {
        Environment.ExitCode = _previousExitCode;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Transient_Fetch_Failures_Are_Retried_And_Indexing_Continues()
    {
        var storage = NewStorage();
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 3);

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 10);
        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
        Assert.True(adapter.Failures >= 3);
    }

    [Fact]
    public async Task An_Idle_Indexer_At_The_Tip_Keeps_The_Published_Timestamp_Moving()
    {
        var storage = NewStorage();
        var (worker, _) = Create(storage, Scenario.Adapter(), maxFailures: 10);

        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        var first = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.UpdatedAt;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((await storage.GetAsync(ChainId.AlgorandTestnet, timeout.Token))!.UpdatedAt <= first)
        {
            await Task.Delay(10, timeout.Token);
        }

        await worker.StopAsync(CancellationToken.None);

        var status = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!;
        Assert.Equal((Scenario.LastRound, IngestionState.Following), (status.IndexedRound, status.State));
    }

    [Fact]
    public async Task The_Failure_Count_Resets_When_Rounds_Were_Indexed_In_Between()
    {
        var storage = NewStorage();

        // Every third fetch fails, so each attempt indexes one round and then fails. With a limit of 3 consecutive
        // failures the worker only survives if indexed rounds reset the count.
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 0, failEvery: 3);
        var (worker, lifetime) = Create(storage, adapter, maxFailures: 3);
        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task A_Run_Of_Failures_Without_Progress_Stops_The_Application()
    {
        var storage = NewStorage();
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: int.MaxValue);

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 3);
        await worker.StartAsync(CancellationToken.None);
        await lifetime.WaitForStopAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.True(lifetime.StopRequested);
        Assert.Equal(1, Environment.ExitCode);
    }

    [Fact]
    public async Task A_Chain_Divergence_Stops_The_Application_Immediately_Without_Retrying()
    {
        var storage = NewStorage();
        var blocks = Scenario.Blocks().ToArray();
        blocks[2] = blocks[2] with { PreviousHash = new byte[32] };
        var adapter = new FlakyAdapter(Scenario.Adapter(blocks), failuresBeforeSuccess: 0);

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 1000);
        await worker.StartAsync(CancellationToken.None);
        await lifetime.WaitForStopAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        Assert.Equal(1, Environment.ExitCode);
    }

    [Fact]
    public async Task A_Balance_Underflow_Stops_The_Application_And_The_Log_Names_The_Chain_The_Last_Round_And_The_Cause()
    {
        var storage = NewStorage();
        var chain = new TestChain();
        // Bob holds 10,000 in the genesis allocation and pays 20,000: the chain could never do this, the index is wrong (issue #115).
        var adapter = Scenario.Adapter([chain.Block(1, [new TestPayment(Scenario.Bob, Scenario.Alice, 20_000)], rewards: Scenario.RewardsAt(1))]);
        var logger = new ListLogger();

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 1000, logger: logger);
        await worker.StartAsync(CancellationToken.None);
        await lifetime.WaitForStopAsync();
        await worker.StopAsync(CancellationToken.None);

        var fatal = Assert.Single(logger.Entries, static e => e.Level == LogLevel.Critical);
        Assert.IsType<BalanceUnderflowException>(fatal.Exception);
        Assert.Contains("algorand-testnet", fatal.Message, StringComparison.Ordinal);
        Assert.Contains("stopped after round 0", fatal.Message, StringComparison.Ordinal);
        Assert.Contains("Balance underflow in round 1", fatal.Message, StringComparison.Ordinal);
        Assert.Equal(1, Environment.ExitCode);
    }

    [Fact]
    public async Task Algod_Api_Errors_And_Timeouts_Are_Retried()
    {
        var storage = NewStorage();
        var errors = new Queue<Exception>(
        [
            new Algorand.ApiException("Service Unavailable", 503, "unavailable", new Dictionary<string, IEnumerable<string>>(), new HttpRequestException("503")),
            new TaskCanceledException("The request timed out"),
        ]);
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 2, errors: errors);

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 10);
        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
        Assert.Equal(2, adapter.Failures);
    }

    [Fact]
    public async Task A_Young_Network_Whose_First_Block_Is_Not_Served_Yet_Is_Waited_For_Not_Failed()
    {
        var storage = NewStorage();
        var notYet = new Queue<Exception>([new BlockNotAvailableException(1), new BlockNotAvailableException(1), new BlockNotAvailableException(1)]);
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 3, errors: notYet);

        // Two failures would stop the worker, so surviving three missing fetches shows they were waited out inside the indexer.
        var (worker, lifetime) = Create(storage, adapter, maxFailures: 2);
        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task A_Database_That_Is_Not_Ready_At_Startup_Is_Waited_For()
    {
        var storage = new SchemaFlakyStorage(failures: 3);

        var (worker, lifetime) = Create(storage, Scenario.Adapter(), maxFailures: 10);
        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
        Assert.Equal(4, storage.SchemaAttempts);
    }

    [Fact]
    public async Task A_Block_The_Archive_Never_Serves_Although_Algod_Is_Far_Ahead_Stops_The_Worker()
    {
        var storage = NewStorage();
        var adapter = Scenario.Adapter();
        adapter.Latest = 1_000;

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 3);
        await worker.StartAsync(CancellationToken.None);
        await lifetime.WaitForStopAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(Scenario.LastRound, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        Assert.Equal(1, Environment.ExitCode);
    }

    [Fact]
    public async Task A_Block_That_Is_Just_Not_Out_Yet_At_The_Tip_Is_Waited_For()
    {
        var storage = NewStorage();
        var chain = new TestChain();
        var blocks = Scenario.Blocks(chain).ToList();
        var next = chain.Block(6, rewards: Scenario.RewardsAt(6));
        var adapter = Scenario.Adapter(blocks);
        adapter.Latest = 6;

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 2);
        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);

        // Wait until the indexer has really asked for the announced block (and not found it), instead of guessing with a sleep.
        var fetchesBefore = adapter.FetchCount;
        await WaitUntilAsync(() => adapter.FetchCount > fetchesBefore);
        adapter.Add(next);
        await WaitForRoundAsync(storage, 6);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(400)]
    public async Task Algod_Client_Errors_Are_Not_Retried(int status)
    {
        var storage = NewStorage();
        var errors = new Queue<Exception>([new Algorand.ApiException("Denied", status, "denied", new Dictionary<string, IEnumerable<string>>(), new HttpRequestException("client error"))]);
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 1, errors: errors);

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 1000);
        await worker.StartAsync(CancellationToken.None);
        await lifetime.WaitForStopAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, adapter.Failures);
        Assert.Equal(1, Environment.ExitCode);
    }

    [Fact]
    public async Task A_Standby_Replica_Writes_Nothing_While_Another_Holds_The_Lease_And_Takes_Over_When_It_Is_Released()
    {
        var storage = NewStorage();
        var leader = await storage.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(leader);

        var (worker, lifetime) = Create(storage, Scenario.Adapter(), maxFailures: 10);
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        Assert.Null(await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));

        await leader.DisposeAsync();
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
        Assert.Equal(2, storage.LeasesGranted);
    }

    [Fact]
    public async Task Losing_The_Lease_Stops_Indexing_And_The_Replica_Resumes_When_It_Gets_The_Lease_Again()
    {
        var storage = NewStorage();
        var chain = new TestChain();
        var blocks = Scenario.Blocks(chain).ToList();
        var next = chain.Block(6, rewards: Scenario.RewardsAt(6));
        var adapter = Scenario.Adapter(blocks);
        var health = new WorkerHealth();
        var (worker, lifetime) = Create(storage, adapter, maxFailures: 10, health);

        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        storage.RevokeLease();
        await WaitUntilAsync(() => health.LeasesLost == 1 && health.State == WorkerState.Leader && storage.LeasesGranted == 2);
        // The tip follows the highest added block, so adding block 6 is all it takes. Announcing round 6 before the block exists (as
        // the test above does on purpose) lets a starved test thread produce enough consecutive failures to stop the worker.
        adapter.Add(next);
        await WaitForRoundAsync(storage, 6);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
        Assert.Equal(2, health.LeasesAcquired);
    }

    [Fact]
    public async Task Losing_The_Lease_During_A_Retry_Backoff_Is_A_Failover_Not_A_Crash()
    {
        var storage = NewStorage();
        var health = new WorkerHealth();
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 1);
        var options = Options.Create(new IndexerOptions
        {
            FetchConcurrency = 1,
            FollowPollInterval = TimeSpan.FromMilliseconds(5),
            RetryInitialBackoff = TimeSpan.FromSeconds(30),
            RetryMaxBackoff = TimeSpan.FromSeconds(30),
            MaxConsecutiveFailures = 10,
            LeaseRetryInterval = TimeSpan.FromMilliseconds(5),
        });
        var indexer = new RoundIndexer(adapter, new AlgoEffectNormalizer(), storage, options, NullLogger<RoundIndexer>.Instance);
        var lifetime = new FakeLifetime();
        var worker = new IndexerWorker(indexer, storage, new ChainContext(ChainId.AlgorandTestnet), options, lifetime, storage, storage, health, NullLogger<IndexerWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => adapter.Failures == 1 && health.State == WorkerState.Leader);
        await Task.Delay(50);
        storage.RevokeLease();
        await WaitUntilAsync(() => health.LeasesLost == 1 && storage.LeasesGranted == 2);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
        Assert.Equal(0, Environment.ExitCode);
    }

    [Fact]
    public async Task A_Commit_That_Fails_Against_The_New_Writer_After_The_Lease_Was_Lost_Is_Not_Fatal()
    {
        var storage = NewStorage();
        var health = new WorkerHealth();
        var errors = new Queue<Exception>([new ChainDivergenceException("another writer moved the cursor")]);
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 1, errors: errors, onFailure: storage.RevokeLease);

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 10, health);
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => health.LeasesLost == 1 && storage.LeasesGranted == 2);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
        Assert.Equal(0, Environment.ExitCode);
    }

    [Fact]
    public async Task A_Divergence_Without_A_Lost_Lease_Still_Stops_The_Application()
    {
        var storage = NewStorage();
        var errors = new Queue<Exception>([new ChainDivergenceException("real divergence")]);
        var adapter = new FlakyAdapter(Scenario.Adapter(), failuresBeforeSuccess: 1, errors: errors);

        var (worker, lifetime) = Create(storage, adapter, maxFailures: 10);
        await worker.StartAsync(CancellationToken.None);
        await lifetime.WaitForStopAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, Environment.ExitCode);
    }

    [Fact]
    public async Task A_Lease_That_Cannot_Be_Requested_Because_The_Database_Is_Down_Is_Waited_For()
    {
        var storage = new LeaseFlakyStorage(failures: 3);

        var (worker, lifetime) = Create(storage, Scenario.Adapter(), maxFailures: 10);
        await worker.StartAsync(CancellationToken.None);
        await WaitForRoundAsync(storage, Scenario.LastRound);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task The_Health_State_Follows_The_Role_Of_The_Replica()
    {
        var storage = NewStorage();
        var other = await storage.TryAcquireAsync(CancellationToken.None);
        var health = new WorkerHealth();
        Assert.False(health.Ready);

        var (worker, _) = Create(storage, Scenario.Adapter(), maxFailures: 10, health);
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => health.State == WorkerState.Standby);
        Assert.True(health.Ready);

        await other!.DisposeAsync();
        await WaitUntilAsync(() => health.State == WorkerState.Leader);
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static InMemoryChainStorage NewStorage() => new(new ChainContext(ChainId.AlgorandTestnet));

    private static (IndexerWorker Worker, FakeLifetime Lifetime) Create(InMemoryChainStorage storage, IChainAdapter adapter, int maxFailures, WorkerHealth? health = null, ILogger<IndexerWorker>? logger = null)
    {
        var options = Options.Create(new IndexerOptions
        {
            FetchConcurrency = 1,
            FollowPollInterval = TimeSpan.FromMilliseconds(5),
            RetryInitialBackoff = TimeSpan.FromMilliseconds(1),
            RetryMaxBackoff = TimeSpan.FromMilliseconds(5),
            MaxConsecutiveFailures = maxFailures,
            LeaseRetryInterval = TimeSpan.FromMilliseconds(5),
        });
        var indexer = new RoundIndexer(adapter, new AlgoEffectNormalizer(), storage, options, NullLogger<RoundIndexer>.Instance);
        var lifetime = new FakeLifetime();
        return (new IndexerWorker(indexer, storage, new ChainContext(ChainId.AlgorandTestnet), options, lifetime, storage, storage, health ?? new WorkerHealth(), logger ?? NullLogger<IndexerWorker>.Instance), lifetime);
    }

    private static async Task WaitForRoundAsync(InMemoryChainStorage storage, ulong round)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var status = await storage.GetAsync(ChainId.AlgorandTestnet, timeout.Token);
            if (status?.IndexedRound >= round)
            {
                return;
            }

            await Task.Delay(5, timeout.Token);
        }
    }

    /// <summary>Fails a number of block fetches (or every n-th fetch) with a transient error before delegating.</summary>
    private sealed class FlakyAdapter(FakeChainAdapter inner, int failuresBeforeSuccess, int failEvery = 0, Queue<Exception>? errors = null, Action? onFailure = null) : IChainAdapter
    {
        private int _calls;
        private int _failures;

        public int Failures => Volatile.Read(ref _failures);

        public ChainProfile Profile => inner.Profile;

        public ReadOnlyMemory<byte> GenesisHash => inner.GenesisHash;

        public Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken) => inner.GetLatestRoundAsync(cancellationToken);

        public Task<GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken) => inner.GetGenesisAsync(cancellationToken);

        public Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            var fail = failEvery > 0 ? call % failEvery == 0 : call <= failuresBeforeSuccess;
            if (fail)
            {
                Interlocked.Increment(ref _failures);
                onFailure?.Invoke();
                var error = errors is { Count: > 0 } ? errors.Dequeue() : new BlockFetchException("simulated outage", round);
                return Task.FromException<BlockData>(error);
            }

            return inner.FetchBlockAsync(round, cancellationToken);
        }
    }

    private sealed class LeaseFlakyStorage(int failures) : InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet))
    {
        private int _attempts;

        public override Task<IWriterLease?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _attempts) <= failures
                ? Task.FromException<IWriterLease?>(new IOException("database is starting"))
                : base.TryAcquireAsync(cancellationToken);
    }

    private sealed class SchemaFlakyStorage(int failures) : InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet))
    {
        private int _attempts;

        public int SchemaAttempts => Volatile.Read(ref _attempts);

        public override Task EnsureSchemaAsync(CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _attempts) <= failures
                ? Task.FromException(new IOException("database is starting"))
                : Task.CompletedTask;
    }

    private sealed class ListLogger : ILogger<IndexerWorker>
    {
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StopRequested => _stopped.Task.IsCompleted;

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopped.TrySetResult();

        public Task WaitForStopAsync() => _stopped.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
