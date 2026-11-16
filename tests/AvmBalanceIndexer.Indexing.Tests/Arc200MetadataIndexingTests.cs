using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.Indexing.Tests;

/// <summary>Issue #175: ARC-200 tokens get name, symbol, decimals and the verification flag from the application, never at the cost of indexing.</summary>
public class Arc200MetadataIndexingTests
{
    private const ulong Application = 55;
    private static readonly TokenKey Key = TokenKey.Arc200(Application);

    private static readonly Arc200Metadata Full = new("Voi Token", "VOI", 6, new BigInteger(1_000_000));

    private sealed class FakeSource(Func<int, Arc200Metadata?> answer) : IArc200MetadataSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public List<(ulong App, int SenderLength, ulong Latest)> Requests { get; } = [];

        public Task<Arc200Metadata?> TryReadAsync(ulong applicationId, ReadOnlyMemory<byte> sender, ulong latestRound, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            lock (Requests)
            {
                Requests.Add((applicationId, sender.Length, latestRound));
            }

            return Task.FromResult(answer(call));
        }
    }

    private static BlockData[] Blocks(int rounds)
    {
        var chain = new TestChain();
        var zero = new Address(new byte[Address.Length]);
        var blocks = new List<BlockData>
        {
            chain.Block(1, [new TestPayment(Alice, Alice, 0, Application: new TestApplication(Application, [TestChain.Arc200TransferLog(zero, Alice, 100)]))], rewards: RewardsAt(1)),
        };
        for (var round = 2; round <= rounds; round++)
        {
            blocks.Add(chain.Block((ulong)round, [new TestPayment(Alice, Alice, 0, Application: new TestApplication(Application, [TestChain.Arc200TransferLog(Alice, Bob, 1)]))], rewards: RewardsAt((ulong)round)));
        }

        return [.. blocks];
    }

    private static async Task<(InMemoryChainStorage Storage, TokenInfo? Token)> IndexAsync(IArc200MetadataSource? source, IndexerOptions? options = null, int rounds = 3)
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var indexer = new RoundIndexer(
            Adapter(Blocks(rounds)),
            new AlgoEffectNormalizer(),
            storage,
            Options.Create(options ?? new IndexerOptions()),
            NullLogger<RoundIndexer>.Instance,
            arc200Metadata: source);
        await indexer.RunAsync((ulong)rounds, CancellationToken.None);
        return (storage, await storage.GetAsync(Key, CancellationToken.None));
    }

    [Fact]
    public async Task A_Token_Gets_Its_Metadata_And_Is_Verified_When_All_Four_Methods_Answered()
    {
        var source = new FakeSource(_ => Full);

        var (_, token) = await IndexAsync(source);

        Assert.NotNull(token);
        Assert.Equal(("Voi Token", "VOI", 6, true), (token.Name, token.Symbol, token.Decimals, token.StandardVerified));
        Assert.Contains("\"totalSupply\":\"1000000\"", token.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\"metadataSource\":\"simulate\"", token.MetadataJson, StringComparison.Ordinal);
        Assert.Equal(1, source.Calls); // once per token, not once per block
        Assert.Equal(32, source.Requests[0].SenderLength); // the fee sink
    }

    [Fact]
    public async Task A_Partial_Answer_Keeps_The_Standard_Unverified()
    {
        var (_, token) = await IndexAsync(new FakeSource(_ => new Arc200Metadata("Voi Token", null, 6, null)));

        Assert.NotNull(token);
        Assert.Equal(("Voi Token", null, 6, false), (token.Name, token.Symbol, token.Decimals, token.StandardVerified));
    }

    [Fact]
    public async Task Indexing_Is_Identical_With_The_Metadata_Off_Or_Failing()
    {
        var (withSource, _) = await IndexAsync(new FakeSource(_ => Full));
        var (without, token) = await IndexAsync(null);
        var (failing, failedToken) = await IndexAsync(new FakeSource(_ => throw new HttpRequestException("algod down")));

        Assert.Equal((null, null, null, false), (token!.Name, token.Symbol, token.Decimals, token.StandardVerified));
        Assert.Equal((null, false), (failedToken!.Name, failedToken.StandardVerified));
        foreach (var storage in new[] { withSource, without, failing })
        {
            Assert.Equal(3UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
            Assert.Equal(98, (await storage.GetBalanceAtRoundAsync(Alice, Key, 3, CancellationToken.None)).Value);
        }
    }

    [Fact]
    public async Task The_Option_Turns_The_Lookup_Off()
    {
        var source = new FakeSource(_ => Full);

        var (_, token) = await IndexAsync(source, new IndexerOptions { Arc200Metadata = false });

        Assert.Equal(0, source.Calls);
        Assert.Null(token!.Name);
    }

    [Fact]
    public async Task A_Node_That_Could_Not_Be_Asked_Is_Asked_Again_Only_After_The_Retry_Interval()
    {
        var source = new FakeSource(call => call == 1 ? null : Full);
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var options = new IndexerOptions { Arc200MetadataRetryInterval = TimeSpan.FromMilliseconds(300), FetchConcurrency = 2 };
        var adapter = Adapter(Blocks(40));
        adapter.Latest = 40;
        var indexer = new RoundIndexer(adapter, new AlgoEffectNormalizer(), storage, Options.Create(options), NullLogger<RoundIndexer>.Instance, arc200Metadata: source);

        // the first rounds are indexed at once: the one failed lookup is not repeated within the interval
        await indexer.RunAsync(5, CancellationToken.None);
        Assert.Equal(1, source.Calls);
        Assert.Null((await storage.GetAsync(Key, CancellationToken.None))!.Name);

        await Task.Delay(400);
        await indexer.RunAsync(8, CancellationToken.None);

        Assert.Equal(2, source.Calls);
        Assert.Equal("Voi Token", (await storage.GetAsync(Key, CancellationToken.None))!.Name);
    }

    [Fact]
    public async Task A_Token_That_Answers_Nothing_Is_Asked_A_Few_Times_And_Then_Left_Alone()
    {
        var source = new FakeSource(_ => new Arc200Metadata(null, null, null, null));
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var options = new IndexerOptions { Arc200MetadataRetryInterval = TimeSpan.FromMilliseconds(100), FetchConcurrency = 2 };
        var indexer = new RoundIndexer(Adapter(Blocks(40)), new AlgoEffectNormalizer(), storage, Options.Create(options), NullLogger<RoundIndexer>.Instance, arc200Metadata: source);

        for (ulong stop = 2; stop <= 20; stop += 2)
        {
            await indexer.RunAsync(stop, CancellationToken.None);
            await Task.Delay(350);
        }

        Assert.Equal(3, source.Calls);
        Assert.Null((await storage.GetAsync(Key, CancellationToken.None))!.Name);
    }

    [Fact]
    public async Task A_Failed_Lookup_Pauses_All_Lookups_So_A_Down_Node_Does_Not_Cost_Every_Block_A_Timeout()
    {
        var chain = new TestChain();
        var zero = new Address(new byte[Address.Length]);
        TestPayment Transfer(ulong application) => new(Alice, Alice, 0, Application: new TestApplication(application, [TestChain.Arc200TransferLog(zero, Alice, 1)]));
        var blocks = new[] { chain.Block(1, [Transfer(1), Transfer(2), Transfer(3)], rewards: RewardsAt(1)), chain.Block(2, [Transfer(4)], rewards: RewardsAt(2)) };
        var source = new FakeSource(_ => null);
        var indexer = new RoundIndexer(
            Adapter(blocks), new AlgoEffectNormalizer(), new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet)),
            Options.Create(new IndexerOptions()), NullLogger<RoundIndexer>.Instance, arc200Metadata: source);

        await indexer.RunAsync(2, CancellationToken.None);

        Assert.Equal(1, source.Calls); // four tokens were pending; the first failure paused the rest
    }

    [Fact]
    public async Task A_Token_With_Stored_Metadata_Is_Not_Asked_Again_After_A_Restart()
    {
        var source = new FakeSource(_ => Full);
        var (storage, _) = await IndexAsync(source, rounds: 2);
        var restarted = new RoundIndexer(
            Adapter(Blocks(4)), new AlgoEffectNormalizer(), storage, Options.Create(new IndexerOptions()), NullLogger<RoundIndexer>.Instance, arc200Metadata: source);

        await restarted.RunAsync(4, CancellationToken.None);

        Assert.Equal(1, source.Calls);
    }
}
