using System.Net;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Worker.Tests;

[Collection("ExitCode")]
public sealed class LedgerAuditorTests : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan ThrottlePause = TimeSpan.FromMilliseconds(400);
    private static readonly Address[] Accounts = [Scenario.Alice, Scenario.Bob, Scenario.Carol, Scenario.Dave];
    private readonly int _previousExitCode = Environment.ExitCode;

    public void Dispose() => Environment.ExitCode = _previousExitCode;

    private static RoundIndexer Indexer(IChainStorage storage) =>
        new(Scenario.Adapter(), new AlgoEffectNormalizer(), storage, Options.Create(new IndexerOptions { FetchConcurrency = 1 }), NullLogger<RoundIndexer>.Instance);

    private static async Task SetStateAsync(InMemoryChainStorage storage, IngestionState state)
    {
        var status = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!;
        await storage.UpdateProgressAsync(status with { State = state, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
    }

    /// <summary>The ledger as it should be: the post-round balances of the accounts, taken from a database that was indexed correctly.</summary>
    private static async Task<FakeDeltas> LedgerOfAsync(InMemoryChainStorage reference)
    {
        var deltas = new FakeDeltas();
        for (ulong round = 0; round <= Scenario.LastRound; round++)
        {
            var algo = new Dictionary<Address, ulong>();
            foreach (var account in Accounts)
            {
                algo[account] = (ulong)(await reference.GetBalanceAtRoundAsync(account, TokenKey.Native, round, CancellationToken.None)).Value;
            }

            deltas.Rounds[round] = new LedgerDelta(algo, new Dictionary<(Address, ulong), ulong?>());
        }

        return deltas;
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required LedgerAuditor Auditor { get; init; }

        public required WorkerHealth Health { get; init; }

        public required CapturingLogger Logger { get; init; }

        public required StopSignal Lifetime { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Auditor.StopAsync(CancellationToken.None);
            Auditor.Dispose();
        }
    }

    private static Rig Start(InMemoryChainStorage storage, ILedgerDeltaSource deltas, AuditOptions audit, bool leader = true)
    {
        var health = new WorkerHealth("audit-test-" + Guid.NewGuid().ToString("N"));
        if (leader)
        {
            health.BecameLeader();
        }

        var logger = new CapturingLogger();
        var lifetime = new StopSignal();
        var auditor = new LedgerAuditor(deltas, storage, new ChainContext(ChainId.AlgorandTestnet), health, Options.Create(new WorkerOptions { Audit = audit }), lifetime, logger) { Interval = Tick, ThrottlePause = ThrottlePause };
        auditor.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new Rig { Auditor = auditor, Health = health, Logger = logger, Lifetime = lifetime };
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    /// <summary>A database at round 2 in the given state, with the correct ledger of the whole scenario, and the rest of the scenario still to be indexed.</summary>
    private static async Task<(InMemoryChainStorage Storage, FakeDeltas Ledger)> AtRoundTwoAsync()
    {
        var reference = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(reference).RunAsync(Scenario.LastRound, CancellationToken.None);
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(storage).RunAsync(2, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        return (storage, await LedgerOfAsync(reference));
    }

    private static async Task FinishTheScenarioAsync(InMemoryChainStorage storage)
    {
        await Indexer(storage).RunAsync(Scenario.LastRound, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
    }

    [Fact]
    public async Task Rounds_That_Agree_Advance_The_Audit_And_Report_No_Mismatch_History_Is_Not_Audited()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100); // activation: the audit starts at round 2, rounds 0 to 2 are history

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal([3UL, 4UL, 5UL], ledger.Requested.Distinct().Order().ToArray());
        Assert.Equal(0, rig.Health.AuditMismatches);
        Assert.False(rig.Lifetime.StopRequested);
        Assert.Null(await storage.GetAuditStopAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_Wrong_Balance_Stops_The_Worker_Records_The_Stop_And_Later_Rounds_Are_Not_Audited()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        // The ledger says Bob holds 1 more than the database at round 4.
        var good = ledger.Rounds[4];
        ledger.Rounds[4] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = good.Algo[Scenario.Bob] + 1 } };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Lifetime.StopRequested);

        Assert.Equal(1, Environment.ExitCode);
        var stop = await storage.GetAuditStopAsync(CancellationToken.None);
        Assert.Equal(4UL, stop!.Round);
        Assert.Equal(Scenario.LastRound, stop.PublishedRound);
        Assert.Contains("indexed 7000, ledger 7001", stop.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(5UL, ledger.Requested);
        Assert.Equal(1, rig.Health.AuditMismatches);
        var error = Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Error);
        Assert.Contains("round 4", error.Message, StringComparison.Ordinal);
        Assert.Contains(ReferenceNodeEncode(Scenario.Bob), error.Message, StringComparison.Ordinal);
        Assert.Contains("indexed 7000", error.Message, StringComparison.Ordinal);
        Assert.Contains("ledger 7001", error.Message, StringComparison.Ordinal);
        var critical = Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Critical);
        Assert.Contains("the last round that agreed is 3", critical.Message, StringComparison.Ordinal);
        Assert.Contains("published up to 5", critical.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_StopOnMismatch_The_Mismatch_Is_Logged_And_Counted_And_The_Audit_Continues()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        var good = ledger.Rounds[3];
        ledger.Rounds[3] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Carol] = 1 } };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1, StopOnMismatch = false });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal(1, rig.Health.AuditMismatches);
        Assert.False(rig.Lifetime.StopRequested);
        Assert.Null(await storage.GetAuditStopAsync(CancellationToken.None));
        Assert.Contains(rig.Logger.Entries, static e => e.Level == LogLevel.Error);
        Assert.DoesNotContain(rig.Logger.Entries, static e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task A_Mismatch_At_An_Ignored_Round_Is_A_Warning_Counted_Separately()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        var good = ledger.Rounds[4];
        ledger.Rounds[4] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = 1 } };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1, IgnoreRounds = [4] });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal((0, 1), (rig.Health.AuditMismatches, rig.Health.AuditIgnored));
        Assert.False(rig.Lifetime.StopRequested);
        Assert.Contains(rig.Logger.Entries, static e => e.Level == LogLevel.Warning && e.Message.Contains("IgnoreRounds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Standby_Replica_Audits_Nothing()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 }, leader: false);
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await Task.Delay(200);

        Assert.Empty(ledger.Requested);
    }

    [Fact]
    public async Task A_Backfilling_Status_Without_Sampling_Audits_Nothing()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        await SetStateAsync(storage, IngestionState.Backfilling);
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1, BackfillSampleEvery = 0 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await SetStateAsync(storage, IngestionState.Backfilling);
        await Task.Delay(200);

        Assert.Empty(ledger.Requested);
    }

    [Fact]
    public async Task A_Backfilling_Writer_Is_Sampled_One_Round_In_Every_Interval_After_Each_Is_Published()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(storage).RunAsync(0, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Backfilling);
        var ledger = new FakeDeltas { Default = new LedgerDelta(new Dictionary<Address, ulong>(), new Dictionary<(Address, ulong), ulong?>()) };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1, BackfillSampleEvery = 100 });
        await Task.Delay(100);

        // The status advances from 0 to 1000 while backfilling; empty rounds are enough, the sample compares the (empty) ledger changes.
        for (ulong round = 1; round <= 1000; round++)
        {
            await storage.WriteRoundAsync(new ChainStatus(ChainId.AlgorandTestnet, round, TestChain.FakeHash(round), null, IngestionState.Backfilling, DateTimeOffset.UtcNow), [], CancellationToken.None);
        }

        await UntilAsync(() => ledger.Requested.Contains(1000UL));

        Assert.Equal(Enumerable.Range(1, 10).Select(static i => (ulong)i * 100).ToArray(), ledger.Requested.Distinct().Order().ToArray());
        Assert.Equal(0, rig.Health.AuditMismatches);
    }

    [Fact]
    public async Task A_Node_Without_Deltas_Switches_The_Audit_Off_Once_Without_An_Error()
    {
        var (storage, _) = await AtRoundTwoAsync();
        var none = new FakeDeltas { AlwaysNull = true };
        await using var rig = Start(storage, none, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        // Thirty more rounds follow; the node has none of their deltas.
        for (ulong round = 3; round <= 32; round++)
        {
            await storage.WriteRoundAsync(new ChainStatus(ChainId.AlgorandTestnet, round, TestChain.FakeHash(round), null, IngestionState.Following, DateTimeOffset.UtcNow), [], CancellationToken.None);
        }

        await UntilAsync(() => rig.Logger.Entries.Any(static e => e.Message.Contains("no ledger deltas", StringComparison.Ordinal)));

        Assert.Single(rig.Logger.Entries, static e => e.Message.Contains("no ledger deltas", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Logger.Entries, static e => e.Level >= LogLevel.Error);
        Assert.False(rig.Lifetime.StopRequested);
        Assert.Equal(0, rig.Health.AuditMismatches);
        Assert.True(rig.Health.AuditSkippedCount >= 20);
    }

    [Fact]
    public async Task A_Node_That_Fails_Is_No_Mismatch_The_Round_Is_Tried_Again()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        ledger.FailuresBeforeSuccess = 3;
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal(0, rig.Health.AuditMismatches);
        Assert.False(rig.Lifetime.StopRequested);
        Assert.True(ledger.Requested.Count(static r => r == 3) >= 4);
        Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Warning); // logged at most every ten minutes
    }

    [Fact]
    public async Task While_Following_One_Round_In_FollowSampleEvery_Is_Audited()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 2 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == 4);
        await Task.Delay(100); // round 5 is published and must not be asked for

        Assert.Equal([4UL], ledger.Requested.ToArray());
        Assert.Equal(0, rig.Health.AuditMismatches);
    }

    [Fact]
    public async Task Following_Without_Sampling_Audits_Nothing()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 0 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await Task.Delay(150);

        Assert.Empty(ledger.Requested);
        Assert.False(rig.Lifetime.StopRequested);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_Node_That_Throttles_Or_Has_No_Quota_Left_Pauses_The_Audit_Which_Resumes_At_The_Published_Round(HttpStatusCode answer)
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        ledger.Throttled.Enqueue(answer);
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        // Round 3 is refused: the audit asks nothing for the length of the pause (many ticks), the indexer needs the quota more.
        await Indexer(storage).RunAsync(3, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        await UntilAsync(() => ledger.Requested.Count == 1);
        await Task.Delay(ThrottlePause / 2);
        Assert.Equal([3UL], ledger.Requested.ToArray());
        Assert.True(rig.Health.AuditSkippedCount >= 1);
        var warning = Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Warning);
        Assert.Contains("the ledger audit pauses", warning.Message, StringComparison.Ordinal); // an expected answer of a public node: one line

        // After the pause it continues with what is published then; the refused round is not asked for again.
        await Task.Delay(ThrottlePause);
        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal([3UL, 4UL, 5UL], ledger.Requested.ToArray());
        Assert.Equal(0, rig.Health.AuditMismatches);
        Assert.False(rig.Lifetime.StopRequested);
    }

    [Fact]
    public async Task A_404_For_A_Young_Round_Is_Tried_Again_It_Is_Not_Skipped_Nor_Counts_Toward_Switching_Off()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        ledger.NotYetAvailable = 3; // the node is a few rounds behind the block source
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.True(ledger.Requested.Count(static r => r == 3) >= 4);
        Assert.Equal(0, rig.Health.AuditSkippedCount);
        Assert.DoesNotContain(rig.Logger.Entries, static e => e.Message.Contains("no ledger deltas", StringComparison.Ordinal));
    }

    private static string ReferenceNodeEncode(Address account) => new Algorand.Address(account.ToArray()).EncodeAsString();

    private sealed class FakeDeltas : ILedgerDeltaSource
    {
        private readonly object _gate = new();
        private readonly List<ulong> _requested = [];
        private int _failures;
        private int _notYet;

        public Dictionary<ulong, LedgerDelta> Rounds { get; } = [];

        public LedgerDelta? Default { get; init; }

        public bool AlwaysNull { get; init; }

        public int FailuresBeforeSuccess { get; set; }

        /// <summary>Answers of a gateway that throttles or has no quota left (403, 429), given before anything else.</summary>
        public Queue<HttpStatusCode> Throttled { get; } = [];

        /// <summary>Answers 404 (null) this many times first, as a node that has not reached a young round yet.</summary>
        public int NotYetAvailable { get; set; }

        public IReadOnlyList<ulong> Requested
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requested];
                }
            }
        }

        public Task<LedgerDelta?> TryGetAsync(ulong round, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _requested.Add(round);
                if (_failures < FailuresBeforeSuccess)
                {
                    _failures++;
                    throw new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable);
                }

                if (Throttled.TryDequeue(out var status))
                {
                    throw new HttpRequestException("throttled", null, status);
                }
            }

            lock (_gate)
            {
                if (_notYet < NotYetAvailable)
                {
                    _notYet++;
                    return Task.FromResult<LedgerDelta?>(null);
                }
            }

            return Task.FromResult(AlwaysNull ? null : Rounds.GetValueOrDefault(round) ?? Default);
        }
    }

    private sealed class StopSignal : IHostApplicationLifetime
    {
        private int _stopped;

        public bool StopRequested => Volatile.Read(ref _stopped) == 1;

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => Interlocked.Exchange(ref _stopped, 1);
    }

    private sealed class CapturingLogger : ILogger<LedgerAuditor>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
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
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
