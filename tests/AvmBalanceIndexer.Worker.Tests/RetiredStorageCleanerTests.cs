using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker.Tests;

public class RetiredStorageCleanerTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Nothing_Is_Removed_While_The_Rebuilt_Data_Is_Still_Backfilling()
    {
        var retired = new FakeRetired("old_keyspace");
        var storage = new StatusStorage { Status = Status(IngestionState.Backfilling) };
        using var health = Leader();
        using var cleaner = Create(retired, storage, health);

        await cleaner.StartAsync(CancellationToken.None);
        await Task.Delay(Settle);
        Assert.Equal(0, retired.Calls);

        // The same service removes it once the indexer has caught up: no restart is needed.
        storage.Status = Status(IngestionState.Following);
        await retired.Removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cleaner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, retired.Calls);
    }

    [Fact]
    public async Task Nothing_Is_Removed_Before_Anything_Was_Indexed()
    {
        var retired = new FakeRetired("old_keyspace");
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = null }, health);

        await cleaner.StartAsync(CancellationToken.None);
        await Task.Delay(Settle);
        await cleaner.StopAsync(CancellationToken.None);

        Assert.Equal(0, retired.Calls);
    }

    [Fact]
    public async Task A_Standby_Replica_Removes_Nothing()
    {
        var retired = new FakeRetired("old_keyspace");
        using var health = new WorkerHealth(Guid.NewGuid().ToString("N"));
        health.BecameStandby();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health);

        await cleaner.StartAsync(CancellationToken.None);
        await Task.Delay(Settle);
        Assert.Equal(0, retired.Calls);

        // It takes over the request together with the writer lease.
        health.BecameLeader();
        await retired.Removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cleaner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_Failing_Removal_Is_Retried_And_Does_Not_End_The_Service()
    {
        var retired = new FakeRetired("old_keyspace") { FailuresBeforeSuccess = 2 };
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health);

        await cleaner.StartAsync(CancellationToken.None);
        await retired.Removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cleaner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, retired.Calls);
    }

    [Fact]
    public async Task A_Status_That_Cannot_Be_Read_Is_Retried()
    {
        var retired = new FakeRetired("old_keyspace");
        var storage = new StatusStorage { Status = Status(IngestionState.Following), ReadFailures = 2 };
        using var health = Leader();
        using var cleaner = Create(retired, storage, health);

        await cleaner.StartAsync(CancellationToken.None);
        await retired.Removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cleaner.StopAsync(CancellationToken.None);

        Assert.Equal(1, retired.Calls);
    }

    [Fact]
    public async Task Storage_That_Is_Still_In_Use_Is_Tried_Again_Until_It_Is_Free()
    {
        var retired = new FakeRetired("old_keyspace") { InUseAnswers = 2 };
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health);

        await cleaner.StartAsync(CancellationToken.None);
        await retired.Removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cleaner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, retired.Calls);
    }

    [Theory]
    [InlineData(RetirementOutcome.Refused)]
    [InlineData(RetirementOutcome.Absent)]
    public async Task A_Request_That_Retrying_Cannot_Change_Is_Asked_Once(RetirementOutcome outcome)
    {
        var retired = new FakeRetired("old_keyspace") { Final = outcome };
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health);

        await cleaner.StartAsync(CancellationToken.None);
        await cleaner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(Settle);

        Assert.Equal(1, retired.Calls);
    }

    [Fact]
    public async Task Without_Retired_Storage_The_Service_Ends_At_Once_And_Reads_Nothing()
    {
        var storage = new StatusStorage { Status = Status(IngestionState.Following) };
        using var health = Leader();
        using var none = Create(null, storage, health);
        using var empty = Create(new FakeRetired(), storage, health);

        await none.StartAsync(CancellationToken.None);
        await empty.StartAsync(CancellationToken.None);
        await none.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await empty.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, storage.Reads);
    }

    private static WorkerHealth Leader()
    {
        var health = new WorkerHealth(Guid.NewGuid().ToString("N"));
        health.BecameLeader();
        return health;
    }

    private static ChainStatus Status(IngestionState state) =>
        new(ChainId.AlgorandTestnet, 10, TestChain.FakeHash(10), 10, state, DateTimeOffset.UnixEpoch);

    private static RetiredStorageCleaner Create(FakeRetired? retired, StatusStorage storage, WorkerHealth health) =>
        new(
            retired is null ? [] : [retired],
            storage,
            new ChainContext(ChainId.AlgorandTestnet),
            health,
            Options.Create(new WorkerOptions()),
            NullLogger<RetiredStorageCleaner>.Instance)
        {
            CheckInterval = TimeSpan.FromMilliseconds(20),
        };

    private sealed class StatusStorage() : InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet))
    {
        private int _reads;
        private int _readFailures;

        public ChainStatus? Status { get; set; }

        public int ReadFailures { set => _readFailures = value; }

        public int Reads => Volatile.Read(ref _reads);

        public override Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            return Interlocked.Decrement(ref _readFailures) >= 0
                ? Task.FromException<ChainStatus?>(new TimeoutException("database away"))
                : Task.FromResult(Status);
        }
    }

    private sealed class FakeRetired(params string[] names) : IRetiredStorage
    {
        private int _calls;

        public int FailuresBeforeSuccess { get; init; }

        public int InUseAnswers { get; init; }

        public RetirementOutcome Final { get; init; } = RetirementOutcome.Removed;

        public TaskCompletionSource Removed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<string> Retired => names;

        public Task<IReadOnlyList<RetirementResult>> RemoveRetiredAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call <= FailuresBeforeSuccess)
            {
                throw new TimeoutException("the drop timed out");
            }

            if (call <= InUseAnswers)
            {
                return Task.FromResult<IReadOnlyList<RetirementResult>>([.. names.Select(static n => new RetirementResult(n, RetirementOutcome.InUse, "a writer holds its lease"))]);
            }

            if (Final == RetirementOutcome.Removed)
            {
                Removed.TrySetResult();
            }

            return Task.FromResult<IReadOnlyList<RetirementResult>>([.. names.Select(n => new RetirementResult(n, Final, Final == RetirementOutcome.Refused ? "another chain" : string.Empty))]);
        }
    }
}
