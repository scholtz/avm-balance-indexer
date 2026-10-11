using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Hosting;
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
        await Task.Delay(50);
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

    private static InMemoryChainStorage NewStorage() => new(new ChainContext(ChainId.AlgorandTestnet));

    private static (IndexerWorker Worker, FakeLifetime Lifetime) Create(InMemoryChainStorage storage, IChainAdapter adapter, int maxFailures)
    {
        var options = Options.Create(new IndexerOptions
        {
            FetchConcurrency = 1,
            FollowPollInterval = TimeSpan.FromMilliseconds(5),
            RetryInitialBackoff = TimeSpan.FromMilliseconds(1),
            RetryMaxBackoff = TimeSpan.FromMilliseconds(5),
            MaxConsecutiveFailures = maxFailures,
        });
        var indexer = new RoundIndexer(adapter, new AlgoEffectNormalizer(), storage, options, NullLogger<RoundIndexer>.Instance);
        var lifetime = new FakeLifetime();
        return (new IndexerWorker(indexer, storage, new ChainContext(ChainId.AlgorandTestnet), options, lifetime, NullLogger<IndexerWorker>.Instance), lifetime);
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
    private sealed class FlakyAdapter(FakeChainAdapter inner, int failuresBeforeSuccess, int failEvery = 0, Queue<Exception>? errors = null) : IChainAdapter
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
                var error = errors is { Count: > 0 } ? errors.Dequeue() : new BlockFetchException("simulated outage", round);
                return Task.FromException<BlockData>(error);
            }

            return inner.FetchBlockAsync(round, cancellationToken);
        }
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
