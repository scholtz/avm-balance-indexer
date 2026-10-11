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

        // Alice pays 6,000 in round 1, nets 0 in round 2 (receives 1,000, pays a 1,000 fee), and pays 1,000 and three asset units in
        // round 3: she must start with 7,000 and 3. Bob never goes negative; the fee sink only collects.
        Assert.Equal(2, seeder.SeededAccounts);
        Assert.Equal(7_000UL, (ulong)(await storage.GetBalanceAtRoundAsync(Alice, TokenKey.Native, 0, CancellationToken.None)).Value);
        Assert.Equal(3UL, (ulong)(await storage.GetBalanceAtRoundAsync(Alice, TokenKey.Asa(77), 0, CancellationToken.None)).Value);
        Assert.True((await storage.GetBalanceAtRoundAsync(Bob, TokenKey.Native, 0, CancellationToken.None)).IsZero);
        Assert.True((await storage.GetBalanceAtRoundAsync(Alice, TokenKey.Native, 3, CancellationToken.None)).IsZero);
        Assert.Equal(3_000UL, (ulong)(await storage.GetBalanceAtRoundAsync(Bob, TokenKey.Native, 3, CancellationToken.None)).Value);
        Assert.Equal(3UL, (ulong)(await storage.GetBalanceAtRoundAsync(Bob, TokenKey.Asa(77), 3, CancellationToken.None)).Value);
        Assert.True((await storage.GetBalanceAtRoundAsync(Alice, TokenKey.Asa(77), 3, CancellationToken.None)).IsZero);
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

    [Fact]
    public void The_Report_Lists_The_Token_Counts_And_The_Holders_Of_The_Top_Tokens()
    {
        var assets = new AssetStatistics(
            new Dictionary<string, int> { ["algo"] = 1, ["asa"] = 40, ["arc200"] = 3 },
            AssetsCreated: 7,
            Arc20Assets: 2,
            [new TopToken("asa:31566704", "USDC", 5_000, 1_234, 2_000), new TopToken("algo:0", "A|B [x](y)" + "\n", 9_000, 900, 900)]);
        var result = new BenchmarkResult(
            "postgres", "PostgreSQL 17", "voi", "2026-10-11", "abc", "machine", 100_000, 1, 100_000,
            new DownloadResult(1, 1, 1), new IndexingResult(1, 1, 1, 1, 0), new StorageSizeResult(1, 1, 1, 1, false, []), [], assets);

        var markdown = Report.Render([result]);

        Assert.Contains("1 native, 3 ARC-200 tokens, 40 ASAs", markdown, StringComparison.Ordinal);
        Assert.Contains("7 ASAs with an effect were created inside the range, 2 of them ARC-20", markdown, StringComparison.Ordinal);
        Assert.Contains("| asa:31566704 | USDC | 5,000 | 1,234 | 2,000 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| algo:0 | A\\|B \\[x\\](y)  | 9,000 |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("66 M", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_Results_There_Is_Nothing_To_Report()
    {
        Assert.Throws<ArgumentException>(() => Report.Render([]));
    }
}
