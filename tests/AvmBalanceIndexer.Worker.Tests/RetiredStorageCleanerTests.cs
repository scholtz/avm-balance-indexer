using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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
    public async Task A_Following_Status_Left_By_A_Previous_Writer_Is_Not_Trusted()
    {
        var retired = new FakeRetired("old_keyspace");
        var stale = Status(IngestionState.Following) with { UpdatedAt = DateTimeOffset.UtcNow - RetiredStorageCleaner.FreshStatus - TimeSpan.FromMinutes(1) };
        var storage = new StatusStorage { Status = stale };
        using var health = Leader();
        using var cleaner = Create(retired, storage, health);

        await cleaner.StartAsync(CancellationToken.None);
        await Task.Delay(Settle);
        Assert.Equal(0, retired.Calls);

        // The new writer publishes again (it is at the tip): now the request goes ahead.
        storage.Status = Status(IngestionState.Following);
        await retired.Removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cleaner.StopAsync(CancellationToken.None);
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

    [Fact]
    public async Task Each_Request_Is_Finished_By_Itself_And_A_Final_Answer_Is_Not_Asked_Again()
    {
        var retired = new FakeRetired("busy", "foreign", "old");
        retired.Outcomes["busy"] = RetirementOutcome.InUse;
        retired.Outcomes["foreign"] = RetirementOutcome.Refused;
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health);

        await cleaner.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (retired.Asked.Count(static name => name == "busy") < 4)
        {
            await Task.Delay(10, timeout.Token);
        }

        await cleaner.StopAsync(CancellationToken.None);

        // The keyspace that is in use is asked at every check; the refused and the removed one only once.
        Assert.Equal(1, retired.Asked.Count(static name => name == "foreign"));
        Assert.Equal(1, retired.Asked.Count(static name => name == "old"));
    }

    [Fact]
    public async Task A_Request_That_Keeps_Failing_Does_Not_Hold_Up_The_Ones_After_It()
    {
        var retired = new FakeRetired("broken", "old");
        retired.Failing.Add("broken");
        var logger = new CapturingLogger();
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health, logger);

        await cleaner.StartAsync(CancellationToken.None);
        await retired.Removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (retired.Asked.Count(static name => name == "broken") < 4)
        {
            await Task.Delay(10, timeout.Token);
        }

        await cleaner.StopAsync(CancellationToken.None);

        Assert.Equal(1, retired.Asked.Count(static name => name == "old"));

        // The deploy job reports this line as a failed request; the repeats are debug detail, not a warning a minute.
        var failures = logger.Lines.Where(static line => line.Text == "Retired storage broken could not be removed; trying again later").ToList();
        Assert.True(failures.Count >= 4);
        Assert.Single(failures, static line => line.Level == LogLevel.Warning);
        Assert.All(failures.Skip(1), static line => Assert.Equal(LogLevel.Debug, line.Level));
    }

    [Fact]
    public async Task A_Keyspace_That_Stays_In_Use_Is_A_Warning_Once_And_Again_When_The_Reason_Changes()
    {
        var retired = new FakeRetired("busy") { Final = RetirementOutcome.InUse };
        var logger = new CapturingLogger();
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health, logger);

        await cleaner.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (retired.Calls < 4)
        {
            await Task.Delay(10, timeout.Token);
        }

        retired.Reason = "its status was last updated later";
        var seen = retired.Calls;
        while (retired.Calls < seen + 3)
        {
            await Task.Delay(10, timeout.Token);
        }

        await cleaner.StopAsync(CancellationToken.None);

        Assert.Equal(2, logger.Lines.Count(static line => line.Level == LogLevel.Warning && line.Text.Contains("still in use", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_Removal_Is_Logged_Even_When_A_Later_Request_Fails()
    {
        var retired = new FakeRetired("old", "broken");
        retired.Failing.Add("broken");
        var logger = new CapturingLogger();
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health, logger);

        await cleaner.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (retired.Asked.Count(static name => name == "broken") < 3)
        {
            await Task.Delay(10, timeout.Token);
        }

        await cleaner.StopAsync(CancellationToken.None);

        Assert.Single(logger.Lines, static line => line.Text.Contains("Retired storage old was removed", StringComparison.Ordinal));
        Assert.Equal(1, retired.Asked.Count(static name => name == "old"));
    }

    /// <summary>
    /// The deploy job (.github/workflows/helm-deploy.yml, step "Database change requests") finds the outcome of a request by
    /// these words in the worker log ("could not be removed" is pinned by the failing-request test above). Change them there too.
    /// </summary>
    [Theory]
    [InlineData(RetirementOutcome.Removed, "was removed", LogLevel.Warning)]
    [InlineData(RetirementOutcome.Absent, "does not exist", LogLevel.Information)]
    [InlineData(RetirementOutcome.Refused, "was not removed", LogLevel.Error)]
    [InlineData(RetirementOutcome.InUse, "still in use", LogLevel.Warning)]
    public async Task The_Outcome_Is_Logged_In_The_Words_The_Deploy_Job_Looks_For(RetirementOutcome outcome, string words, LogLevel level)
    {
        var retired = new FakeRetired("old_keyspace") { Final = outcome };
        var logger = new CapturingLogger();
        using var health = Leader();
        using var cleaner = Create(retired, new StatusStorage { Status = Status(IngestionState.Following) }, health, logger);

        await cleaner.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!logger.Lines.Any(line => line.Text.Contains(words, StringComparison.Ordinal)))
        {
            await Task.Delay(10, timeout.Token);
        }

        await cleaner.StopAsync(CancellationToken.None);

        var logged = logger.Lines.First(line => line.Text.Contains(words, StringComparison.Ordinal));
        Assert.StartsWith("Retired storage old_keyspace ", logged.Text, StringComparison.Ordinal);
        Assert.Equal(level, logged.Level);
    }

    private static WorkerHealth Leader()
    {
        var health = new WorkerHealth(Guid.NewGuid().ToString("N"));
        health.BecameLeader();
        return health;
    }

    private static ChainStatus Status(IngestionState state) =>
        new(ChainId.AlgorandTestnet, 10, TestChain.FakeHash(10), 10, state, DateTimeOffset.UtcNow);

    private static RetiredStorageCleaner Create(IRetiredStorage? retired, StatusStorage storage, WorkerHealth health, ILogger<RetiredStorageCleaner>? logger = null) =>
        new(
            retired is null ? [] : [retired],
            storage,
            new ChainContext(ChainId.AlgorandTestnet),
            health,
            Options.Create(new WorkerOptions()),
            logger ?? NullLogger<RetiredStorageCleaner>.Instance)
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

        /// <summary>Outcome per name, for requests that end differently; names not listed end with <see cref="Final"/>.</summary>
        public Dictionary<string, RetirementOutcome> Outcomes { get; } = new(StringComparer.Ordinal);

        /// <summary>Names whose removal always throws.</summary>
        public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

        public ConcurrentQueue<string> Asked { get; } = new();

        public string Reason { get; set; } = "another chain";

        public TaskCompletionSource Removed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<string> Retired => names;

        public Task<RetirementResult> RemoveRetiredAsync(string name, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            Asked.Enqueue(name);
            if (call <= FailuresBeforeSuccess || Failing.Contains(name))
            {
                throw new TimeoutException("the drop timed out");
            }

            if (call <= InUseAnswers)
            {
                return Task.FromResult(new RetirementResult(name, RetirementOutcome.InUse, "a writer holds its lease"));
            }

            var outcome = Outcomes.GetValueOrDefault(name, Final);
            if (outcome == RetirementOutcome.Removed)
            {
                Removed.TrySetResult();
            }

            return Task.FromResult(new RetirementResult(name, outcome, outcome is RetirementOutcome.Refused or RetirementOutcome.InUse ? Reason : string.Empty));
        }
    }

    private sealed class CapturingLogger : ILogger<RetiredStorageCleaner>
    {
        public ConcurrentQueue<(LogLevel Level, string Text)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue((logLevel, formatter(state, exception)));
    }
}
