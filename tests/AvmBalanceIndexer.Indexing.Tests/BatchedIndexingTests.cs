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
