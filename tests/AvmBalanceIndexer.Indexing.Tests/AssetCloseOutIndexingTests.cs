using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.Indexing.Tests;

/// <summary>
/// Issue #115: the TestNet worker stopped on a balance underflow because early blocks do not state the amount an asset
/// close-out moves. Whole rounds go through the normalizer, the tracker and storage here.
/// </summary>
public class AssetCloseOutIndexingTests
{
    private const ulong Asset = 100;

    private static async Task<InMemoryChainStorage> IndexAsync(params BlockData[] blocks)
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var indexer = new RoundIndexer(Adapter(blocks), new AlgoEffectNormalizer(), storage, Options.Create(new IndexerOptions()), NullLogger<RoundIndexer>.Instance);
        await indexer.RunAsync((ulong)blocks.Length, CancellationToken.None);
        return storage;
    }

    private static async Task<ulong> BalanceAsync(InMemoryChainStorage storage, Address account, ulong round) =>
        (ulong)(await storage.GetBalanceAtRoundAsync(account, TokenKey.Asa(Asset), round, CancellationToken.None)).Value;

    [Fact]
    public async Task A_Close_Out_Without_A_Stated_Amount_Credits_The_Destination_So_It_Can_Spend_It()
    {
        var chain = new TestChain();
        var storage = await IndexAsync(
            chain.Block(1, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(Asset, 1_000))], rewards: RewardsAt(1)),
            chain.Block(2, [new TestPayment(Bob, Bob, 0, Asset: TestAsset.OptIn(Asset))], rewards: RewardsAt(2)),
            chain.Block(3, [new TestPayment(Alice, Bob, 0, Asset: TestAsset.Transfer(Asset, 300))], rewards: RewardsAt(3)),
            // The block does not say how much Bob's close-out moves (no aca): all 300 units go back to Alice.
            chain.Block(4, [new TestPayment(Bob, Bob, 0, Asset: TestAsset.CloseOut(Asset, Alice, closingAmount: 0))], rewards: RewardsAt(4)),
            // Before the fix the indexer believed Alice held 700 and stopped here with a balance underflow.
            chain.Block(5, [new TestPayment(Alice, Carol, 0, Asset: TestAsset.Transfer(Asset, 1_000))], rewards: RewardsAt(5)));

        Assert.Equal((700UL, 300UL), (await BalanceAsync(storage, Alice, 3), await BalanceAsync(storage, Bob, 3)));
        Assert.Equal((1_000UL, 0UL), (await BalanceAsync(storage, Alice, 4), await BalanceAsync(storage, Bob, 4)));
        Assert.Equal((0UL, 0UL, 1_000UL), (await BalanceAsync(storage, Alice, 5), await BalanceAsync(storage, Bob, 5), await BalanceAsync(storage, Carol, 5)));

        var closer = Assert.Single((await storage.GetHoldingsAsync(Bob, true, null, 10, CancellationToken.None)).Items, h => h.Token == TokenKey.Asa(Asset));
        Assert.False(closer.OptedIn);
        var history = await storage.GetEffectsAsync(Alice, TokenKey.Asa(Asset), null, null, null, 10, CancellationToken.None);
        Assert.Contains(history.Items, e => e.Type == EffectType.AssetTransfer && e.Delta == 300 && e.Counterparty == Bob);
    }
}
