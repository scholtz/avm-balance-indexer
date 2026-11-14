using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.Indexing.Tests;

public class BatchedIndexingTests
{
    private static readonly IndexerOptions Backfill = new()
    {
        FetchConcurrency = 2,
        CommitBatchRounds = 3,
        FollowingThresholdRounds = 0,
        FollowPollInterval = TimeSpan.FromMilliseconds(100),
    };

    private static RoundIndexer Indexer(IChainStorage storage, Adapters.IChainAdapter adapter, IndexerOptions? options = null) =>
        new(adapter, new AlgoEffectNormalizer(), storage, Options.Create(options ?? Backfill), NullLogger<RoundIndexer>.Instance);

    [Fact]
    public async Task Rounds_Are_Committed_In_Batches_While_Backfilling()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));

        var result = await Indexer(storage, Adapter()).RunAsync(LastRound, CancellationToken.None);

        Assert.Equal((long)LastRound, result.RoundsIndexed);
        Assert.Equal([1, 3, 2], storage.BatchSizes); // genesis, then rounds 1 to 3 and 4 to 5
    }

    [Fact]
    public async Task Following_The_Tip_Tells_The_Fetcher_Where_The_Tip_Is()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var adapter = Adapter();
        adapter.Latest = LastRound;
        using var stop = new CancellationTokenSource();

        var run = Indexer(storage, adapter, new IndexerOptions { FetchConcurrency = 2, FollowPollInterval = TimeSpan.FromMilliseconds(10) }).RunAsync(null, stop.Token);
        while ((await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound < LastRound)
        {
            await Task.Delay(10);
        }

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.NotEmpty(adapter.TipHints);
        Assert.All(adapter.TipHints, hint => Assert.Equal(LastRound, hint));
    }

    [Fact]
    public async Task Waiting_For_A_Block_The_Nodes_Do_Not_Serve_Does_Not_Refresh_The_Published_Status()
    {
        // At the tip a block comes every few seconds: a writer that gets none must let the status age, so the API reports it as
        // stalled (#168). Only a writer that has caught up (no newer round exists) refreshes it.
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var adapter = Adapter();
        adapter.Latest = LastRound + 1; // the chain is one round ahead of what any node serves
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = Indexer(storage, adapter, new IndexerOptions { FetchConcurrency = 2, FollowPollInterval = TimeSpan.FromMilliseconds(10) }).RunAsync(null, stop.Token);
        while ((await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound < LastRound)
        {
            await Task.Delay(10, stop.Token);
        }

        var caughtUp = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!;
        var fetches = adapter.FetchCount;
        while (adapter.FetchCount < fetches + 5)
        {
            await Task.Delay(10, stop.Token); // several more attempts at the missing block
        }

        var later = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!;
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(caughtUp.UpdatedAt, later.UpdatedAt);
    }

    [Fact]
    public async Task A_Writer_Resuming_At_The_Tip_Fetches_Its_Seed_Block_With_The_Tip_Hint()
    {
        // The seed block (the last published round) is re-fetched on every start; at the tip relays can serve it (#169, ADR 0019).
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(storage, Adapter()).RunAsync(LastRound, CancellationToken.None);
        var restarted = Adapter();
        restarted.Latest = LastRound;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var run = Indexer(storage, restarted, new IndexerOptions { FetchConcurrency = 2, FollowPollInterval = TimeSpan.FromMilliseconds(10) }).RunAsync(null, stop.Token);
        while (restarted.FetchCount == 0)
        {
            await Task.Delay(10, stop.Token);
        }

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal([LastRound], restarted.TipHints); // the seed fetch, the only fetch of a writer that is already caught up
    }

    [Fact]
    public async Task A_Wrong_Seed_Block_From_A_Relay_Does_Not_Stop_The_Writer_When_The_Archival_Nodes_Serve_The_Right_One()
    {
        // With the tip hint the seed may come from a public relay. A different (self-consistent) block from there must not end in a
        // "chain divergence, rebuild the database": the archival nodes decide.
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(storage, Adapter()).RunAsync(LastRound, CancellationToken.None);
        var foreign = Blocks(new TestChain { TxnCounterBefore = 1_000 }).Single(static b => b.Round == LastRound);
        var restarted = new RelayServes(Adapter(), foreign) { Latest = LastRound };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var run = Indexer(storage, restarted, new IndexerOptions { FetchConcurrency = 2, FollowPollInterval = TimeSpan.FromMilliseconds(10) }).RunAsync(null, stop.Token);
        while (restarted.ArchivalFetches == 0 && !run.IsCompleted)
        {
            await Task.Delay(10, stop.Token);
        }

        await Task.Delay(100, CancellationToken.None);
        Assert.False(run.IsCompleted, run.Exception?.ToString()); // still following, not stopped by a divergence
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    /// <summary>Serves <paramref name="relayBlock"/> for fetches with a tip hint (as a relay could), the real chain otherwise.</summary>
    private sealed class RelayServes(FakeChainAdapter archive, Adapters.BlockData relayBlock) : Adapters.IChainAdapter
    {
        private int _archivalFetches;

        public ulong? Latest { get => archive.Latest; set => archive.Latest = value; }

        public int ArchivalFetches => Volatile.Read(ref _archivalFetches);

        public Adapters.ChainProfile Profile => archive.Profile;

        public ReadOnlyMemory<byte> GenesisHash => archive.GenesisHash;

        public Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken) => archive.GetLatestRoundAsync(cancellationToken);

        public Task<Adapters.GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken) => archive.GetGenesisAsync(cancellationToken);

        public Task<Adapters.BlockData> FetchBlockAsync(ulong round, ulong latestRound, CancellationToken cancellationToken) =>
            round == relayBlock.Round ? Task.FromResult(relayBlock) : archive.FetchBlockAsync(round, latestRound, cancellationToken);

        public Task<Adapters.BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _archivalFetches);
            return archive.FetchBlockAsync(round, cancellationToken);
        }
    }

    [Fact]
    public async Task A_Bounded_Resume_Fetches_Its_Seed_Block_Without_A_Tip_Hint()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        await Indexer(storage, Adapter()).RunAsync(LastRound - 2, CancellationToken.None);
        var resumed = Adapter();

        await Indexer(storage, resumed).RunAsync(LastRound, CancellationToken.None);

        Assert.True(resumed.FetchCount > 0);
        Assert.Empty(resumed.TipHints); // a bounded run's target is not the chain tip
    }

    [Fact]
    public async Task A_Backfill_Pass_Gives_The_Fetcher_No_Tip_Because_The_Tip_Moves_On_During_It()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var adapter = Adapter();
        adapter.Latest = LastRound;
        using var stop = new CancellationTokenSource();
        var options = new IndexerOptions { FetchConcurrency = 2, FollowingThresholdRounds = 1, FollowPollInterval = TimeSpan.FromMilliseconds(10) };

        var run = Indexer(storage, adapter, options).RunAsync(null, stop.Token); // starts LastRound rounds behind: a backfill pass
        while ((await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound < LastRound)
        {
            await Task.Delay(10);
        }

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Empty(adapter.TipHints);
    }

    [Fact]
    public async Task A_Bounded_Run_Gives_The_Fetcher_No_Tip_Because_Its_Target_Is_Not_The_Tip()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var adapter = Adapter();

        await Indexer(storage, adapter).RunAsync(LastRound, CancellationToken.None);

        Assert.Empty(adapter.TipHints);
    }

    [Fact]
    public async Task Near_The_Tip_Every_Round_Is_Committed_On_Its_Own()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var following = new IndexerOptions { FetchConcurrency = 2, CommitBatchRounds = 3, FollowingThresholdRounds = 50 };

        await Indexer(storage, Adapter(), following).RunAsync(LastRound, CancellationToken.None);

        Assert.Equal([1, 1, 1, 1, 1, 1], storage.BatchSizes);
    }

    [Fact]
    public async Task A_Failed_Batch_Publishes_None_Of_Its_Rounds()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet)) { FailBatchesOver = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Indexer(storage, Adapter()).RunAsync(LastRound, CancellationToken.None));

        Assert.Equal(0UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task A_Hole_In_The_Archive_Is_Reported_After_The_Rounds_Before_It_Were_Written()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var adapter = Adapter(Blocks().Take(3)); // rounds 4 and 5 are never served
        adapter.Latest = LastRound;
        var options = new IndexerOptions { FetchConcurrency = 2, CommitBatchRounds = 10, FollowingThresholdRounds = 0 };

        await Assert.ThrowsAsync<Adapters.BlockNotAvailableException>(() => Indexer(storage, adapter, options).RunAsync(null, CancellationToken.None));

        Assert.Equal(3UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        Assert.Equal([1, 3], storage.BatchSizes);
    }

    [Fact]
    public async Task A_Failing_Fetch_Does_Not_Throw_Away_The_Rounds_Computed_Before_It()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var flaky = new FailingAdapter(Adapter(), failingRound: 4);
        var options = new IndexerOptions { FetchConcurrency = 1, CommitBatchRounds = 10, FollowingThresholdRounds = 0 };

        await Assert.ThrowsAsync<TimeoutException>(() => Indexer(storage, flaky, options).RunAsync(LastRound, CancellationToken.None));

        Assert.Equal(3UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        Assert.Equal([1, 3], storage.BatchSizes);
    }

    [Fact]
    public async Task Cancellation_Does_Not_Write_Anything_Because_The_Writer_Lease_May_Be_Lost()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var cancelled = new FailingAdapter(Adapter(), failingRound: 4, new OperationCanceledException());
        var options = new IndexerOptions { FetchConcurrency = 1, CommitBatchRounds = 10, FollowingThresholdRounds = 0 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Indexer(storage, cancelled, options).RunAsync(LastRound, CancellationToken.None));

        Assert.Equal(0UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task A_Bounded_Run_That_Meets_A_Hole_Also_Writes_The_Rounds_Before_It()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet));
        var options = new IndexerOptions { FetchConcurrency = 1, CommitBatchRounds = 10, FollowingThresholdRounds = 0 };

        await Assert.ThrowsAsync<Adapters.BlockNotAvailableException>(() => Indexer(storage, Adapter(Blocks().Take(3)), options).RunAsync(LastRound, CancellationToken.None));

        Assert.Equal(3UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task A_Failing_Write_Is_Not_Written_Again_While_Failing()
    {
        var storage = new RecordingStorage(new ChainContext(ChainId.AlgorandTestnet)) { FailBatchesOver = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Indexer(storage, Adapter()).RunAsync(LastRound, CancellationToken.None));

        Assert.Equal(1, storage.FailedAttempts); // one refused batch, no second attempt from the failure handler
    }

    private sealed class FailingAdapter(Adapters.IChainAdapter inner, ulong failingRound, Exception? failure = null) : Adapters.IChainAdapter
    {
        public Adapters.ChainProfile Profile => inner.Profile;

        public ReadOnlyMemory<byte> GenesisHash => inner.GenesisHash;

        public Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken) => inner.GetLatestRoundAsync(cancellationToken);

        public Task<Adapters.GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken) => inner.GetGenesisAsync(cancellationToken);

        public Task<Adapters.BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken) =>
            round == failingRound ? Task.FromException<Adapters.BlockData>(failure ?? new TimeoutException("host timed out")) : inner.FetchBlockAsync(round, cancellationToken);
    }

    [Fact]
    public async Task Staged_Balances_Are_Seen_By_The_Next_Round_And_Cached_Only_After_The_Commit()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var tracker = new BalanceTracker(storage, 1_000);
        BalanceDelta Delta(int round, long amount) =>
            new(new EffectId((ulong)round, 0, "tx", [], 0), TestChain.Account(1), TokenKey.Native, EffectType.Genesis, new BigInteger(amount), null);

        var first = await tracker.ApplyAsync([Delta(1, 50)], CancellationToken.None);
        tracker.Stage(first);
        var second = await tracker.ApplyAsync([Delta(2, -20)], CancellationToken.None);

        Assert.Equal(30UL, (ulong)second.Effects.Single().BalanceAfter.Value);

        tracker.Stage(second);
        tracker.CommitStaged();
        var third = await tracker.ApplyAsync([Delta(3, 1)], CancellationToken.None);

        Assert.Equal(31UL, (ulong)third.Effects.Single().BalanceAfter.Value);
    }

    private sealed class RecordingStorage(ChainContext context) : InMemoryChainStorage(context)
    {
        public List<int> BatchSizes { get; } = [];

        public int? FailBatchesOver { get; init; }

        public int FailedAttempts { get; private set; }

        public override Task<bool> WriteRoundsAsync(IReadOnlyList<RoundWrite> rounds, CancellationToken cancellationToken)
        {
            if (FailBatchesOver is { } limit && rounds.Count > limit)
            {
                FailedAttempts++;
                throw new InvalidOperationException("disk full");
            }

            BatchSizes.Add(rounds.Count);
            return base.WriteRoundsAsync(rounds, cancellationToken);
        }
    }
}
