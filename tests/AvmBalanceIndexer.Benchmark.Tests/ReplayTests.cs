using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Benchmark.Tests;

public class ReplayTests
{
    private static readonly TestChain Chain = new();
    private static readonly Address Alice = TestChain.Account(1);
    private static readonly Address Bob = TestChain.Account(2);

    /// <summary>Blocks of a chain that is already running: the accounts hold balances nobody told the indexer about.</summary>
    private static BlockSet MidChainRange()
    {
        var blocks = new List<Indexing.Adapters.BlockData>
        {
            Chain.Block(500),
            Chain.Block(501, [new TestPayment(Alice, Bob, 5_000, Fee: 1_000)]),
            Chain.Block(502, [new TestPayment(Bob, Alice, 1_000, Fee: 1_000), new TestPayment(Alice, Alice, 0, Asset: TestAsset.OptIn(77))]),
            Chain.Block(503, [new TestPayment(Alice, Bob, 0, Asset: TestAsset.Transfer(77, 3))]),
        };
        return BlockSet.FromBlocks(Chain.Profile, blocks, 501);
    }

    [Fact]
    public async Task A_Mid_Chain_Range_Replays_From_Balances_Seeded_By_A_Dry_Run()
    {
        var set = MidChainRange();
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var normalizer = new LenientNormalizer(new AlgoEffectNormalizer(), set.RewardsBeforeFirstRound);
        var seeder = new Seeder();
        seeder.DryRun(set, normalizer);
        await seeder.WriteGenesisAsync(storage, set, CancellationToken.None);

        var result = await new RoundIndexer(set, normalizer, storage, Options.Create(new IndexerOptions()), NullLogger<RoundIndexer>.Instance)
            .RunAsync((ulong)set.Rounds, CancellationToken.None);

        Assert.Equal(3, set.Rounds);
        Assert.Equal((1UL, 3UL), (result.FirstRound, result.LastRound));

        // Alice needs 6,000 for the first payment and fee, and the three asset units she later sends.
        Assert.True(seeder.SeededAccounts >= 2);
        var native = await storage.GetBalanceAtRoundAsync(Alice, TokenKey.Native, 3, CancellationToken.None);
        Assert.True(native.Value >= 0);
        Assert.Equal(3UL, (ulong)(await storage.GetBalanceAtRoundAsync(Bob, TokenKey.Asa(77), 3, CancellationToken.None)).Value);
    }

    [Fact]
    public void Replayed_Rounds_Are_Numbered_From_One_And_Linked_To_Each_Other()
    {
        var set = MidChainRange();

        var blocks = set.Blocks.ToArray();

        Assert.Equal([1UL, 2UL, 3UL], blocks.Select(static b => b.Round));
        Assert.Equal(set.BeforeFirstHash, blocks[0].PreviousHash);
        Assert.Equal(blocks[0].Hash, blocks[1].PreviousHash);
        Assert.Equal(blocks[1].Hash, blocks[2].PreviousHash);
    }

    [Fact]
    public void The_Report_Compares_Providers_Side_By_Side()
    {
        BenchmarkResult Result(string provider, double rate, long bytes) => new(
            provider, provider + " 1", "algorand-mainnet", "2026-10-11", "abcdef0123456789", "test machine", 10_000, 100, 10_099,
            new DownloadResult(10, 1000, 5), new IndexingResult(100, rate, 1_000_000, 10_000, 5),
            new StorageSizeResult(bytes, 1_000_000, 100, 10, false, [new TableSize("effects", bytes)]),
            [new EndpointResult("balance", "/b", 1, 2, 3, 16, 1000, 4, 100, 0), new EndpointResult("holders", "/h", 5, 6, 7, 16, 200, 8, 100, 2)]);

        var markdown = Report.Render([Result("postgres", 90, 600 * 1048576L), Result("cassandra", 80, 150 * 1048576L)]);

        Assert.Contains("| | cassandra 1 | postgres 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("150.0 MiB | 600.0 MiB", markdown, StringComparison.Ordinal);
        Assert.Contains("**2 errors**", markdown, StringComparison.Ordinal);
        Assert.Contains("abcdef01", markdown, StringComparison.Ordinal);
    }
}
