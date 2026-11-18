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
    private static readonly TimeSpan ThrottlePause = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan QuotaPauseFirst = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan QuotaPause = TimeSpan.FromMilliseconds(300);
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
        var auditor = new LedgerAuditor(deltas, storage, new ChainContext(ChainId.AlgorandTestnet), health, Options.Create(new WorkerOptions { Audit = audit }), lifetime, logger) { Interval = Tick, ThrottlePause = ThrottlePause, QuotaPauseFirst = QuotaPauseFirst, QuotaPause = QuotaPause };
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
        Assert.Contains("last audited round that agreed: 3", stop.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(5UL, ledger.Requested);
        Assert.Equal(1, rig.Health.AuditMismatches);
        var error = Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Error);
        Assert.Contains("round 4", error.Message, StringComparison.Ordinal);
        Assert.Contains(ReferenceNodeEncode(Scenario.Bob), error.Message, StringComparison.Ordinal);
        Assert.Contains("indexed 7000", error.Message, StringComparison.Ordinal);
        Assert.Contains("ledger 7001", error.Message, StringComparison.Ordinal);
        var critical = Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Critical);
        Assert.Contains("the last audited round that agreed is 3", critical.Message, StringComparison.Ordinal);
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
        // No sampling at the tip either: finishing the scenario passes through the state Following for a moment.
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 0, BackfillSampleEvery = 0 });
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

    [Theory]
    [InlineData(1)]
    [InlineData(10)] // sampling does not delay it: every round is asked for until a first one was compared
    public async Task A_Node_Without_Deltas_Switches_The_Audit_Off_Once_Without_An_Error(int followSampleEvery)
    {
        var (storage, _) = await AtRoundTwoAsync();
        var none = new FakeDeltas { AlwaysNull = true };
        await using var rig = Start(storage, none, new AuditOptions { FollowSampleEvery = followSampleEvery });
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

        Assert.Equal([3UL, 4UL], ledger.Requested.ToArray()); // round 3 is the first comparison (asked for whatever the sampling), then every second round
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

    [Fact]
    public async Task A_Node_That_Throttles_Is_Asked_For_The_Same_Round_Again_After_A_Pause_Nothing_Is_Skipped()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        ledger.Throttled.Enqueue(HttpStatusCode.TooManyRequests);
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal([3UL, 3UL, 4UL, 5UL], ledger.Requested.ToArray());
        Assert.DoesNotContain(rig.Logger.Entries, static e => e.Level >= LogLevel.Warning);
        Assert.Equal(0, rig.Health.AuditSkippedCount);
    }

    [Fact]
    public async Task A_Node_That_Keeps_Throttling_A_Round_Is_Given_Up_On_For_That_Round_Like_Any_Failure()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        for (var i = 0; i < 5; i++)
        {
            ledger.Throttled.Enqueue(HttpStatusCode.TooManyRequests);
        }

        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal([3UL, 3UL, 3UL, 3UL, 3UL, 4UL, 5UL], ledger.Requested.ToArray());
        Assert.Equal(1, rig.Health.AuditSkippedCount);
        Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Quota_Answers_In_A_Row_Lengthen_The_Pause_Up_To_Its_Limit()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        ledger.Throttled.Enqueue(HttpStatusCode.Forbidden);
        ledger.Throttled.Enqueue(HttpStatusCode.Forbidden);
        ledger.Throttled.Enqueue(HttpStatusCode.Forbidden);
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        // Each resumed audit finds a newly published round, and is refused again.
        for (ulong round = 3; round <= 5; round++)
        {
            var resumes = (int)(round - 3);
            await UntilAsync(() => rig.Logger.Entries.Count(static e => e.Message.Contains("continues after its pause", StringComparison.Ordinal)) == resumes);
            await Task.Delay(50);
            await Indexer(storage).RunAsync(round, CancellationToken.None);
            await SetStateAsync(storage, IngestionState.Following);
            await UntilAsync(() => rig.Logger.Entries.Count(static e => e.Message.Contains("pauses for", StringComparison.Ordinal)) == resumes + 1);
        }

        // One Warning for the outage; the further pauses are Information.
        Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Warning);
        var pauses = rig.Logger.Entries.Where(static e => e.Message.Contains("pauses for", StringComparison.Ordinal)).Select(static e => e.Message).ToArray();
        Assert.Equal(3, pauses.Length);
        Assert.Contains("pauses for 00:00:00.2000000", pauses[0], StringComparison.Ordinal);
        Assert.Contains("pauses for 00:00:00.3000000", pauses[1], StringComparison.Ordinal); // doubled, and limited
        Assert.Contains("pauses for 00:00:00.3000000", pauses[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Sampled_Older_Round_That_Disagrees_Does_Not_Name_A_Later_Round_As_Good()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1, BackfillSampleEvery = 3 });
        await Task.Delay(100);
        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound); // rounds 3 to 5 agreed

        // The writer falls behind and backfills: round 3 is sampled, and the ledger now disagrees there.
        var good = ledger.Rounds[3];
        ledger.Rounds[3] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = good.Algo[Scenario.Bob] + 1 } };
        await SetStateAsync(storage, IngestionState.Backfilling);
        await UntilAsync(() => rig.Lifetime.StopRequested);

        var stop = await storage.GetAuditStopAsync(CancellationToken.None);
        Assert.Equal(3UL, stop!.Round);
        Assert.Contains("last audited round that agreed: none known below that round", stop.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Quota_Answer_Does_Not_Drop_A_Mismatch_Whose_Stop_Could_Not_Be_Recorded_Yet()
    {
        // Requests: round 3 agrees, round 4 disagrees but the stop cannot be recorded, round 4 again gets the quota answer, then round 4 once more.
        var reference = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(reference).RunAsync(Scenario.LastRound, CancellationToken.None);
        var storage = new RecordFailsOnce();
        await Indexer(storage).RunAsync(2, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        var correct = await LedgerOfAsync(reference);
        var ledger = new FakeDeltas { QuotaAtRequest = 3 };
        foreach (var (round, delta) in correct.Rounds)
        {
            ledger.Rounds[round] = delta;
        }

        var good = ledger.Rounds[4];
        ledger.Rounds[4] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = good.Algo[Scenario.Bob] + 1 } };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Lifetime.StopRequested);

        Assert.Equal([3UL, 4UL, 4UL, 4UL], ledger.Requested.ToArray());
        Assert.Equal(4UL, (await storage.GetAuditStopAsync(CancellationToken.None))!.Round);
        Assert.Equal(1, rig.Health.AuditMismatches); // counted once
    }

    /// <summary>A storage whose published status a test can replace for a while (the published round moving back, as after a rewind).</summary>
    private sealed class Rewindable() : InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet))
    {
        private ChainStatus? _published;

        public ChainStatus? Published
        {
            get => Volatile.Read(ref _published);
            set => Volatile.Write(ref _published, value);
        }

        public override Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken) =>
            Published is { } published ? Task.FromResult<ChainStatus?>(published) : base.GetAsync(chain, cancellationToken);
    }

    [Fact]
    public async Task Sampling_Does_Not_Skip_A_Mismatch_Whose_Stop_Could_Not_Be_Recorded_Yet()
    {
        // Round 3 is the first comparison (asked for whatever the sampling) and disagrees; the stop cannot be recorded at first.
        // Round 3 is not a sampled round (one in 10), and must be looked at again all the same.
        var reference = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(reference).RunAsync(Scenario.LastRound, CancellationToken.None);
        var storage = new RecordFailsOnce();
        await Indexer(storage).RunAsync(2, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        var ledger = await LedgerOfAsync(reference);
        var good = ledger.Rounds[3];
        ledger.Rounds[3] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = good.Algo[Scenario.Bob] + 1 } };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 10 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Lifetime.StopRequested);

        Assert.Equal([3UL, 3UL], ledger.Requested.ToArray());
        Assert.Equal(3UL, (await storage.GetAuditStopAsync(CancellationToken.None))!.Round);
    }

    [Fact]
    public async Task A_Mismatch_Whose_Stop_Could_Not_Be_Recorded_Is_Not_Given_Up_On_When_The_Node_Then_Fails_Again_And_Again()
    {
        // Round 3 disagrees and the stop cannot be recorded; the next six requests for it fail (more than the retry budget of a round).
        var reference = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(reference).RunAsync(Scenario.LastRound, CancellationToken.None);
        var storage = new RecordFailsOnce();
        await Indexer(storage).RunAsync(2, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        var correct = await LedgerOfAsync(reference);
        var ledger = new FakeDeltas { UnavailableAtRequests = [2, 3, 4, 5, 6, 7] };
        foreach (var (round, delta) in correct.Rounds)
        {
            ledger.Rounds[round] = delta;
        }

        var good = ledger.Rounds[3];
        ledger.Rounds[3] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = good.Algo[Scenario.Bob] + 1 } };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Lifetime.StopRequested);

        Assert.Equal(Enumerable.Repeat(3UL, 8).ToArray(), ledger.Requested.ToArray());
        Assert.Equal(3UL, (await storage.GetAuditStopAsync(CancellationToken.None))!.Round);
    }

    private sealed class RecordFailsOnce() : InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet))
    {
        private int _attempts;

        public override Task RecordAuditStopAsync(AuditStop auditStop, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _attempts) == 1 ? Task.FromException(new IOException("database is busy")) : base.RecordAuditStopAsync(auditStop, cancellationToken);
    }

    [Fact]
    public async Task After_The_Published_Round_Moved_Back_A_Round_That_Agreed_Before_Is_Not_Named_As_Good()
    {
        var reference = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(reference).RunAsync(Scenario.LastRound, CancellationToken.None);
        var ledger = await LedgerOfAsync(reference);
        var storage = new Rewindable();
        await Indexer(storage).RunAsync(2, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1, BackfillSampleEvery = 0 });
        await Task.Delay(100);
        await Indexer(storage).RunAsync(3, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        await UntilAsync(() => rig.Health.LastAuditedRound == 3); // round 3 agreed

        // A rewind to round 1, then the replay: the node has no deltas for rounds 2 and 3 any more, and round 4 disagrees.
        var status = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!;
        storage.Published = status with { IndexedRound = 1, State = IngestionState.Backfilling };
        await Task.Delay(100);
        storage.Published = null;
        ledger.Rounds.Remove(2);
        ledger.Rounds.Remove(3);
        var good = ledger.Rounds[4];
        ledger.Rounds[4] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = good.Algo[Scenario.Bob] + 1 } };
        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Lifetime.StopRequested);

        var stop = await storage.GetAuditStopAsync(CancellationToken.None);
        Assert.Equal(4UL, stop!.Round);
        Assert.Contains("last audited round that agreed: none known below that round", stop.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_403_That_Is_Not_About_The_Quota_Is_A_Node_That_Does_Not_Answer_The_Round_Is_Tried_Again_Without_A_Pause()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        var refusing = new FakeDeltas { QuotaAnswer = false };
        foreach (var (round, delta) in ledger.Rounds)
        {
            refusing.Rounds[round] = delta;
        }

        refusing.Throttled.Enqueue(HttpStatusCode.Forbidden);
        refusing.Throttled.Enqueue(HttpStatusCode.Forbidden);
        await using var rig = Start(storage, refusing, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Health.LastAuditedRound == (long)Scenario.LastRound);

        Assert.Equal([3UL, 3UL, 3UL, 4UL, 5UL], refusing.Requested.ToArray());
        var warning = Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain("quota", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Mismatch_Without_An_Audited_Round_Below_It_Names_No_Good_Round()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        var good = ledger.Rounds[3];
        ledger.Rounds[3] = good with { Algo = new Dictionary<Address, ulong>(good.Algo) { [Scenario.Bob] = good.Algo[Scenario.Bob] + 1 } };
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        await FinishTheScenarioAsync(storage);
        await UntilAsync(() => rig.Lifetime.StopRequested);

        var stop = await storage.GetAuditStopAsync(CancellationToken.None);
        Assert.Equal(3UL, stop!.Round);
        Assert.Contains("last audited round that agreed: none known below that round", stop.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Node_Whose_Daily_Quota_Is_Used_Up_Pauses_The_Audit_Which_Resumes_At_The_Published_Round()
    {
        var (storage, ledger) = await AtRoundTwoAsync();
        ledger.Throttled.Enqueue(HttpStatusCode.Forbidden);
        await using var rig = Start(storage, ledger, new AuditOptions { FollowSampleEvery = 1 });
        await Task.Delay(100);

        // Round 3 is refused: the audit asks nothing for the length of the pause (many ticks), the indexer needs the quota more.
        await Indexer(storage).RunAsync(3, CancellationToken.None);
        await SetStateAsync(storage, IngestionState.Following);
        await UntilAsync(() => ledger.Requested.Count == 1);
        await Task.Delay(QuotaPauseFirst / 2);
        Assert.Equal([3UL], ledger.Requested.ToArray());
        Assert.True(rig.Health.AuditSkippedCount >= 1);
        var warning = Assert.Single(rig.Logger.Entries, static e => e.Level == LogLevel.Warning);
        Assert.Contains("the ledger audit pauses", warning.Message, StringComparison.Ordinal); // an expected answer of a public node: one line

        // After the pause it continues with what is published then; the refused round is not asked for again.
        await UntilAsync(() => rig.Logger.Entries.Any(static e => e.Level == LogLevel.Information && e.Message.Contains("continues after its pause", StringComparison.Ordinal)));
        await Task.Delay(50); // the pass that resumed has read the published round (3)
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

        /// <summary>The requests (counted from 1) that fail with a 503.</summary>
        public HashSet<int> UnavailableAtRequests { get; init; } = [];

        /// <summary>The request (counted from 1) that gets the quota answer; 0 for none.</summary>
        public int QuotaAtRequest { get; init; }

        /// <summary>Whether a 403 is the quota answer (as the algod client reports it) or a plain 403.</summary>
        public bool QuotaAnswer { get; init; } = true;

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

                if (UnavailableAtRequests.Contains(_requested.Count))
                {
                    throw new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable);
                }

                if (_requested.Count == QuotaAtRequest)
                {
                    throw new NodeQuotaExceededException("The node answered HTTP 403 for /v2/deltas: Daily free API quota exceeded");
                }

                if (Throttled.TryDequeue(out var status))
                {
                    throw status == HttpStatusCode.Forbidden && QuotaAnswer
                        ? new NodeQuotaExceededException("The node answered HTTP 403 for /v2/deltas/3: Daily free API quota exceeded")
                        : new HttpRequestException("refused", null, status);
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
