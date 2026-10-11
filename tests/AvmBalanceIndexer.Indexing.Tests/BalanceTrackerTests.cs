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

    [Fact]
    public async Task A_Round_Larger_Than_A_Generation_Is_Not_Cached_And_Invalidates_Stale_Entries()
    {
        var storage = await SeededAsync();
        var tracker = new BalanceTracker(storage, 4);
        var warm = await tracker.ApplyAsync([Delta(0, Alice, -10)], CancellationToken.None);
        tracker.Commit(warm);

        // Three accounts reach the generation size of two, so none of them may be cached; Alice's stale entry must go too.
        var big = await tracker.ApplyAsync([Delta(0, Alice, -20), Delta(1, TestChain.Account(60), 1), Delta(2, TestChain.Account(61), 1)], CancellationToken.None);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 1, new byte[32], null, IngestionState.Following, DateTimeOffset.UnixEpoch),
            big.Effects,
            CancellationToken.None);
        tracker.Commit(big);
        var next = await tracker.ApplyAsync([Delta(0, Alice, -5)], CancellationToken.None);

        // The large round took Alice from 90 (cached) to 70 and wrote that; storage therefore says 70 and the next balance is 65.
        // Had her stale cached 90 survived the large round, the result would have been 85.
        Assert.Equal(65UL, (ulong)next.Effects[0].BalanceAfter.Value);
    }

    private static BalanceDelta Arc200(int ordinal, Address account, long amount, EffectType type = EffectType.Arc200Transfer, Address? counterparty = null) =>
        new(new EffectId(1, 0, "tx", [], ordinal), account, TokenKey.Arc200(9), type, new BigInteger(amount), counterparty);

    [Fact]
    public async Task An_Arc200_Debit_Beyond_The_Known_Balance_Is_Limited_Together_With_Its_Credit()
    {
        var tracker = new BalanceTracker(await SeededAsync(), 1000);

        var round = await tracker.ApplyAsync(
            [Arc200(0, Alice, 40, EffectType.Arc200Mint), Arc200(1, Alice, -100, counterparty: Bob), Arc200(2, Bob, 100, counterparty: Alice)],
            CancellationToken.None);

        Assert.Equal([40L, -40L, 40L], round.Effects.Select(static e => (long)e.Delta));
        Assert.Equal([40UL, 0UL, 40UL], round.Effects.Select(static e => (ulong)e.BalanceAfter.Value));
    }

    [Fact]
    public async Task An_Arc200_Debit_From_An_Empty_Account_Leaves_No_Effect()
    {
        var tracker = new BalanceTracker(await SeededAsync(), 1000);

        var round = await tracker.ApplyAsync([Arc200(0, Alice, -5, counterparty: Bob), Arc200(1, Bob, 5, counterparty: Alice), Arc200(2, Bob, -1, EffectType.Arc200Burn)], CancellationToken.None);

        Assert.Empty(round.Effects);
    }
}

