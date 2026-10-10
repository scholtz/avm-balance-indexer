using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Indexing.Tests;

public class BalanceTrackerTests
{
    private static readonly Address Alice = TestChain.Account(1);
    private static readonly Address Bob = TestChain.Account(2);

    private static BalanceDelta Delta(int ordinal, Address account, long amount) =>
        new(new EffectId(1, 0, "tx", [], ordinal), account, TokenKey.Native, EffectType.Payment, new BigInteger(amount), null);

    private static async Task<InMemoryChainStorage> SeededAsync()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var genesis = new TokenEffect(new EffectId(0, 0, string.Empty, [], 0), Alice, TokenKey.Native, EffectType.Genesis, 100, Amount.From(100), null);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, new byte[32], null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [genesis],
            CancellationToken.None);
        return storage;
    }

    [Fact]
    public async Task Computes_Running_Balances_In_Order()
    {
        var tracker = new BalanceTracker(await SeededAsync(), 1000);

        var round = await tracker.ApplyAsync([Delta(0, Alice, -30), Delta(1, Bob, 30), Delta(2, Alice, -20), Delta(3, Bob, 20)], CancellationToken.None);

        Assert.Equal([70UL, 30UL, 50UL, 50UL], round.Effects.Select(static e => (ulong)e.BalanceAfter.Value));
    }

    [Fact]
    public async Task Rejects_A_Delta_That_Would_Make_A_Balance_Negative()
    {
        var tracker = new BalanceTracker(await SeededAsync(), 1000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => tracker.ApplyAsync([Delta(0, Alice, -101)], CancellationToken.None));
    }

    [Fact]
    public async Task Uncommitted_Rounds_Do_Not_Change_Later_Results()
    {
        var tracker = new BalanceTracker(await SeededAsync(), 1000);

        _ = await tracker.ApplyAsync([Delta(0, Alice, -60)], CancellationToken.None);
        var again = await tracker.ApplyAsync([Delta(0, Alice, -60)], CancellationToken.None);

        Assert.Equal(40UL, (ulong)again.Effects[0].BalanceAfter.Value);
    }

    [Fact]
    public async Task Committed_Rounds_Feed_The_Next_Round()
    {
        var tracker = new BalanceTracker(await SeededAsync(), 1000);

        var first = await tracker.ApplyAsync([Delta(0, Alice, -60)], CancellationToken.None);
        tracker.Commit(first);
        var second = await tracker.ApplyAsync([Delta(0, Alice, -10)], CancellationToken.None);

        Assert.Equal(30UL, (ulong)second.Effects[0].BalanceAfter.Value);
    }

    [Fact]
    public async Task Evicted_Entries_Are_Reloaded_From_Storage()
    {
        var storage = await SeededAsync();
        var tracker = new BalanceTracker(storage, 2);
        var first = await tracker.ApplyAsync([Delta(0, Alice, -60), Delta(1, Bob, 60)], CancellationToken.None);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 1, new byte[32], null, IngestionState.Following, DateTimeOffset.UnixEpoch),
            first.Effects,
            CancellationToken.None);
        tracker.Commit(first);

        // Push both accounts out of both generations with unrelated accounts.
        for (var i = 0; i < 4; i++)
        {
            var filler = await tracker.ApplyAsync([Delta(0, TestChain.Account(100 + i), 1)], CancellationToken.None);
            tracker.Commit(filler);
        }

        var second = await tracker.ApplyAsync([Delta(0, Alice, -10)], CancellationToken.None);

        Assert.Equal(30UL, (ulong)second.Effects[0].BalanceAfter.Value);
    }

    [Fact]
    public async Task Recently_Used_Entries_Survive_A_Generation_Rollover()
    {
        var storage = await SeededAsync();
        var tracker = new BalanceTracker(storage, 4);
        var first = await tracker.ApplyAsync([Delta(0, Alice, -60)], CancellationToken.None);
        tracker.Commit(first);

        // Fill the young generation so it rolls over; Alice is now in the old generation and is not lost.
        var filler = await tracker.ApplyAsync([Delta(0, TestChain.Account(50), 1), Delta(1, TestChain.Account(51), 1)], CancellationToken.None);
        tracker.Commit(filler);
        var again = await tracker.ApplyAsync([Delta(0, Alice, -10)], CancellationToken.None);

        // Storage still says 100 (the first round was never written), so 30 proves the cache supplied the value.
        Assert.Equal(30UL, (ulong)again.Effects[0].BalanceAfter.Value);
    }
}
