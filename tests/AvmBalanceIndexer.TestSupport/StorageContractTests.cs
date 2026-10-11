using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// The storage contract. Every provider runs this identical suite by deriving from it, which is what guarantees the
/// same balances, holders, history ordering, replay and publication behaviour regardless of the database.
/// </summary>
public abstract class StorageContractTests
{
    private static readonly TokenKey Native = TokenKey.Native;

    /// <summary>Creates an empty, schema-initialised storage for one chain.</summary>
    protected abstract Task<IChainStorage> CreateStorageAsync();

    protected static RoundIndexer CreateIndexer(IChainStorage storage, IChainAdapter adapter) =>
        new(
            adapter,
            new AlgoEffectNormalizer(),
            storage,
            Options.Create(new IndexerOptions { FetchConcurrency = 3, BalanceCacheEntries = 1_000 }),
            NullLogger<RoundIndexer>.Instance);

    private async Task<IChainStorage> IndexedAsync(ulong upTo = LastRound)
    {
        var storage = await CreateStorageAsync();
        var result = await CreateIndexer(storage, Adapter()).RunAsync(upTo, CancellationToken.None);
        Assert.Equal(upTo, result.LastRound);
        return storage;
    }

    [Fact]
    public async Task Indexing_Publishes_The_Cursor_Of_The_Last_Round()
    {
        var storage = await IndexedAsync();

        var status = await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(LastRound, status.IndexedRound);
        Assert.Equal(Blocks()[(int)LastRound - 1].Hash, status.IndexedBlockHash.ToArray());
    }

    [Fact]
    public async Task Native_Token_Metadata_Is_Stored()
    {
        var storage = await IndexedAsync(1);

        var token = await storage.GetAsync(Native, CancellationToken.None);

        Assert.NotNull(token);
        Assert.Equal(("ALGO", 6, TokenStandard.Native), (token.Symbol, token.Decimals, token.Standard));
    }

    [Fact]
    public async Task Current_Holdings_Match_The_Final_Balances_And_Hide_Zero_Unless_Requested()
    {
        var storage = await IndexedAsync();

        foreach (var account in new[] { Alice, Bob, Dave, Chain.FeeSink, Chain.Pool })
        {
            var page = await storage.GetHoldingsAsync(account, false, null, 10, CancellationToken.None);
            var holding = Assert.Single(page.Items);
            Assert.Equal(ExpectedBalance(account, LastRound), (ulong)holding.Amount.Value);
            Assert.Equal(LastRound, page.IndexedRound);
        }

        Assert.Empty((await storage.GetHoldingsAsync(Carol, false, null, 10, CancellationToken.None)).Items);
        var zero = Assert.Single((await storage.GetHoldingsAsync(Carol, true, null, 10, CancellationToken.None)).Items);
        Assert.True(zero.Amount.IsZero);
        Assert.True(zero.OptedIn);
    }

    [Fact]
    public async Task Historical_Balances_Are_End_Of_Round_Including_Rounds_Without_Activity()
    {
        var storage = await IndexedAsync();

        foreach (var account in new[] { Alice, Bob, Carol, Dave, Chain.FeeSink, Chain.Pool })
        {
            for (ulong round = 0; round <= LastRound; round++)
            {
                var balance = await storage.GetBalanceAtRoundAsync(account, Native, round, CancellationToken.None);
                Assert.Equal(ExpectedBalance(account, round), (ulong)balance.Value);
            }
        }
    }

    [Fact]
    public async Task Unknown_Accounts_Have_A_Zero_Balance()
    {
        var storage = await IndexedAsync();

        var balance = await storage.GetBalanceAtRoundAsync(TestChain.Account(99), Native, 3, CancellationToken.None);

        Assert.True(balance.IsZero);
    }

    [Fact]
    public async Task Reading_Before_Anything_Is_Indexed_Says_So()
    {
        var storage = await CreateStorageAsync();

        await Assert.ThrowsAsync<ChainNotStartedException>(() => storage.GetBalanceAtRoundAsync(Alice, Native, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ChainNotStartedException>(() => storage.GetHoldersAtRoundAsync(Native, 0, false, null, 10, CancellationToken.None));
        Assert.Null(await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("0g")]
    public async Task A_Malformed_Holder_Cursor_Is_An_Argument_Error(string cursor)
    {
        var storage = await IndexedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetHoldersAtRoundAsync(Native, 3, false, cursor, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Reading_A_Round_Beyond_The_Cursor_Is_Rejected()
    {
        var storage = await IndexedAsync(3);

        var balance = await Assert.ThrowsAsync<RoundNotIndexedException>(
            () => storage.GetBalanceAtRoundAsync(Alice, Native, 4, CancellationToken.None));
        var holders = await Assert.ThrowsAsync<RoundNotIndexedException>(
            () => storage.GetHoldersAtRoundAsync(Native, 4, false, null, 10, CancellationToken.None));

        Assert.Equal((4UL, 3UL), (balance.RequestedRound, balance.IndexedRound));
        Assert.Equal(4UL, holders.RequestedRound);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public async Task Holders_Sum_To_The_Supply_At_Every_Round(ulong round)
    {
        var storage = await IndexedAsync();

        var holders = await storage.GetHoldersAtRoundAsync(Native, round, false, null, 100, CancellationToken.None);

        Assert.Null(holders.NextCursor);
        Assert.Equal(SupplyAt(round), holders.Items.Aggregate(0UL, static (sum, h) => sum + (ulong)h.Amount.Value));
        Assert.All(holders.Items, h => Assert.Equal(ExpectedBalance(h.Account, round), (ulong)h.Amount.Value));
        Assert.True(holders.Items.All(static h => !h.Amount.IsZero));
    }

    [Fact]
    public async Task Holders_Are_Paged_In_Account_Order_Without_Gaps_Or_Duplicates()
    {
        var storage = await IndexedAsync();

        var collected = new List<Holding>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await storage.GetHoldersAtRoundAsync(Native, 5, false, cursor, 2, CancellationToken.None);
            collected.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal([Alice, Bob, Dave, Chain.Pool, Chain.FeeSink], collected.Select(static h => h.Account));
    }

    [Fact]
    public async Task Zero_Balance_Accounts_Are_Included_Only_On_Request_And_Only_After_They_Were_Touched()
    {
        var storage = await IndexedAsync();

        var atRound4 = await storage.GetHoldersAtRoundAsync(Native, 4, true, null, 100, CancellationToken.None);
        var atRound3 = await storage.GetHoldersAtRoundAsync(Native, 3, true, null, 100, CancellationToken.None);
        var withoutZero = await storage.GetHoldersAtRoundAsync(Native, 4, false, null, 100, CancellationToken.None);

        Assert.Contains(atRound4.Items, h => h.Account == Carol && h.Amount.IsZero);
        Assert.DoesNotContain(withoutZero.Items, h => h.Account == Carol);
        Assert.DoesNotContain(atRound3.Items, h => h.Account == Dave);
        Assert.Contains(atRound3.Items, h => h.Account == Carol && !h.Amount.IsZero);
    }

    [Fact]
    public async Task Account_History_Is_Newest_First_And_Paged_Without_Gaps()
    {
        var storage = await IndexedAsync();

        var all = new List<TokenEffect>();
        string? cursor = null;
        do
        {
            var page = await storage.GetEffectsAsync(Alice, Native, null, null, cursor, 2, CancellationToken.None);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        var keys = all.Select(static e => (e.Id.Round, e.Id.IntraRoundOrder, e.Id.Ordinal)).ToArray();
        Assert.Equal(keys.OrderByDescending(static k => k), keys);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(1 + 3 + 1 + 2, all.Count);
        Assert.Equal(ExpectedBalance(Alice, LastRound), (ulong)all[0].BalanceAfter.Value);
        Assert.Equal(EffectType.Genesis, all[^1].Type);
    }

    [Fact]
    public async Task A_From_Round_Beyond_Every_Indexed_Round_Matches_Nothing()
    {
        var storage = await IndexedAsync();

        var page = await storage.GetEffectsAsync(Alice, Native, ulong.MaxValue, null, null, 50, CancellationToken.None);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Account_History_Honours_The_Round_Range()
    {
        var storage = await IndexedAsync();

        var page = await storage.GetEffectsAsync(Bob, Native, 3, 4, null, 50, CancellationToken.None);

        Assert.Equal([3UL, 3UL], page.Items.Select(static e => e.Id.Round));
        Assert.Equal([EffectType.Payment, EffectType.Fee], page.Items.Select(static e => e.Type));
        Assert.Equal([7_000UL, 12_000UL], page.Items.Select(static e => (ulong)e.BalanceAfter.Value));
    }

    [Fact]
    public async Task History_Rows_Carry_Transaction_Identity_Inner_Path_And_Counterparty()
    {
        var storage = await IndexedAsync();

        var page = await storage.GetEffectsAsync(Dave, Native, 5, 5, null, 10, CancellationToken.None);

        var effect = Assert.Single(page.Items);
        Assert.Equal(EffectType.Payment, effect.Type);
        Assert.Equal([0], effect.Id.InnerPath);
        Assert.Equal(52, effect.Id.TransactionId.Length);
        Assert.Equal(Bob, effect.Counterparty);
        Assert.Equal(10UL, (ulong)effect.Delta);
    }

    [Fact]
    public async Task Replaying_A_Published_Round_Is_A_No_Op()
    {
        var storage = await IndexedAsync();
        var before = await storage.GetHoldersAtRoundAsync(Native, LastRound, true, null, 100, CancellationToken.None);
        var replay = await CreateIndexer(storage, Adapter()).RunAsync(LastRound, CancellationToken.None);

        var status = await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);
        var after = await storage.GetHoldersAtRoundAsync(Native, LastRound, true, null, 100, CancellationToken.None);

        Assert.Equal(0, replay.RoundsIndexed);
        Assert.Equal(LastRound, status!.IndexedRound);
        Assert.Equal(before.Items.Select(static h => (h.Account, h.Amount)), after.Items.Select(static h => (h.Account, h.Amount)));
    }

    [Fact]
    public async Task Writing_The_Latest_Published_Round_Again_Is_A_No_Op()
    {
        var storage = await IndexedAsync();
        var before = await storage.GetEffectsAsync(Alice, Native, null, null, null, 100, CancellationToken.None);
        var block = Blocks()[(int)LastRound - 1];
        var effects = (await new BalanceTracker(storage, 100).ApplyAsync(new AlgoEffectNormalizer().Normalize(block, RewardsAt(LastRound - 1)), CancellationToken.None)).Effects;
        var status = new ChainStatus(ChainId.AlgorandTestnet, LastRound, block.Hash, null, IngestionState.Following, DateTimeOffset.UnixEpoch);

        // The effects are derived against the current balances on purpose: a published round must be ignored entirely.
        await storage.WriteRoundAsync(status, effects, CancellationToken.None);

        var after = await storage.GetEffectsAsync(Alice, Native, null, null, null, 100, CancellationToken.None);
        Assert.Equal(before.Items.Select(static e => (e.Id, e.BalanceAfter)), after.Items.Select(static e => (e.Id, e.BalanceAfter)));
        var current = await storage.GetHoldingsAsync(Alice, false, null, 10, CancellationToken.None);
        Assert.Equal(ExpectedBalance(Alice, LastRound), (ulong)Assert.Single(current.Items).Amount.Value);
    }

    [Fact]
    public async Task The_Writer_Reports_Whether_It_Applied_The_Round()
    {
        var storage = await CreateStorageAsync();
        var blocks = Blocks();
        var genesis = new ChainStatus(ChainId.AlgorandTestnet, 0, blocks[0].PreviousHash, null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch);

        var first = await storage.WriteRoundAsync(genesis, [], CancellationToken.None);
        var again = await storage.WriteRoundAsync(genesis, [], CancellationToken.None);

        Assert.True(first);
        Assert.False(again);
    }

    [Fact]
    public async Task A_Second_Indexer_On_The_Same_Chain_Is_Stopped_Instead_Of_Corrupting_The_Cache()
    {
        var storage = await CreateStorageAsync();
        await CreateIndexer(storage, Adapter()).RunAsync(2, CancellationToken.None);

        // Both indexers resume from round 2 and race to publish round 3. The loser must stop: either its write finds the round
        // already published (a divergence), or it read balances after the winner committed and fails earlier on arithmetic.
        // What it must never do is leave anything but the correct state behind.
        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(async _ =>
            {
                try
                {
                    await CreateIndexer(storage, Adapter()).RunAsync(LastRound, CancellationToken.None);
                    return (Exception?)null;
                }
                catch (Exception ex) when (ex is ChainDivergenceException or InvalidOperationException)
                {
                    return ex;
                }
            }));

        Assert.Contains(outcomes, static e => e is null);
        Assert.Equal(LastRound, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        foreach (var account in new[] { Alice, Bob, Carol, Dave, Chain.FeeSink, Chain.Pool })
        {
            Assert.Equal(
                ExpectedBalance(account, LastRound),
                (ulong)(await storage.GetBalanceAtRoundAsync(account, Native, LastRound, CancellationToken.None)).Value);
        }
    }

    [Fact]
    public async Task Writing_A_Round_Older_Than_The_Cursor_Is_Refused_And_Changes_Nothing()
    {
        var storage = await IndexedAsync();
        var block = Blocks()[2];
        var status = new ChainStatus(ChainId.AlgorandTestnet, 3, block.Hash, null, IngestionState.Following, DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<ChainDivergenceException>(() => storage.WriteRoundAsync(status, [], CancellationToken.None));

        Assert.Equal(LastRound, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        var page = await storage.GetEffectsAsync(Bob, Native, 3, 3, null, 50, CancellationToken.None);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task A_Different_Hash_For_The_Published_Round_Is_Divergence()
    {
        var storage = await IndexedAsync();
        var status = new ChainStatus(ChainId.AlgorandTestnet, LastRound, new byte[32], null, IngestionState.Following, DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<ChainDivergenceException>(() => storage.WriteRoundAsync(status, [], CancellationToken.None));
    }

    [Fact]
    public async Task Skipping_A_Round_Is_Rejected_And_Nothing_Is_Published()
    {
        var storage = await IndexedAsync(3);
        var status = new ChainStatus(ChainId.AlgorandTestnet, 5, TestChain.FakeHash(5), null, IngestionState.Following, DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<ChainDivergenceException>(() => storage.WriteRoundAsync(status, [], CancellationToken.None));

        Assert.Equal(3UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task The_First_Round_Written_Must_Be_Genesis()
    {
        var storage = await CreateStorageAsync();
        var status = new ChainStatus(ChainId.AlgorandTestnet, 1, TestChain.FakeHash(1), null, IngestionState.Following, DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<ChainDivergenceException>(() => storage.WriteRoundAsync(status, [], CancellationToken.None));
    }

    [Fact]
    public async Task Resuming_From_The_Stored_Cursor_Gives_The_Same_Result_As_A_Straight_Run()
    {
        var resumed = await CreateStorageAsync();
        await CreateIndexer(resumed, Adapter()).RunAsync(2, CancellationToken.None);
        var second = await CreateIndexer(resumed, Adapter()).RunAsync(LastRound, CancellationToken.None);
        var straight = await IndexedAsync();

        Assert.Equal((3UL, LastRound, 3L), (second.FirstRound, second.LastRound, second.RoundsIndexed));
        foreach (var account in new[] { Alice, Bob, Carol, Dave, Chain.FeeSink, Chain.Pool })
        {
            for (ulong round = 0; round <= LastRound; round++)
            {
                Assert.Equal(
                    await straight.GetBalanceAtRoundAsync(account, Native, round, CancellationToken.None),
                    await resumed.GetBalanceAtRoundAsync(account, Native, round, CancellationToken.None));
            }
        }
    }

    [Fact]
    public async Task A_Block_That_Does_Not_Link_To_The_Published_Chain_Is_Refused_And_Not_Published()
    {
        var storage = await CreateStorageAsync();
        await CreateIndexer(storage, Adapter()).RunAsync(3, CancellationToken.None);
        var blocks = Blocks().ToArray();
        var forged = blocks[3] with { PreviousHash = new byte[32] };
        blocks[3] = forged;

        await Assert.ThrowsAsync<ChainDivergenceException>(() => CreateIndexer(storage, Adapter(blocks)).RunAsync(5, CancellationToken.None));

        Assert.Equal(3UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task A_Bounded_Run_Does_Not_Need_Algod()
    {
        var storage = await CreateStorageAsync();
        var adapter = Adapter();
        adapter.AlgodDown = true;

        var result = await CreateIndexer(storage, adapter).RunAsync(3, CancellationToken.None);

        Assert.Equal(3UL, result.LastRound);
    }

    [Fact]
    public async Task A_Genesis_That_Does_Not_Hash_To_The_Pinned_Hash_Is_Refused_Before_Anything_Is_Written()
    {
        var storage = await CreateStorageAsync();
        var adapter = new FakeChainAdapter(Chain.Profile, Genesis with { Hash = new byte[32] });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => CreateIndexer(storage, adapter).RunAsync(1, CancellationToken.None));

        Assert.Contains("genesis", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Fact]
    public async Task Resuming_At_Genesis_Against_A_Different_First_Block_Is_Refused()
    {
        var storage = await CreateStorageAsync();
        await CreateIndexer(storage, Adapter()).RunAsync(0, CancellationToken.None);
        var blocks = Blocks().ToArray();
        blocks[0] = blocks[0] with { PreviousHash = new byte[32] };

        await Assert.ThrowsAsync<ChainDivergenceException>(() => CreateIndexer(storage, Adapter(blocks)).RunAsync(LastRound, CancellationToken.None));

        Assert.Equal(0UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task Resuming_Against_A_Source_That_Serves_A_Different_Cursor_Block_Is_Refused()
    {
        var storage = await CreateStorageAsync();
        await CreateIndexer(storage, Adapter()).RunAsync(3, CancellationToken.None);
        var blocks = Blocks().ToArray();
        blocks[2] = blocks[2] with { Hash = new byte[32] };

        await Assert.ThrowsAsync<ChainDivergenceException>(() => CreateIndexer(storage, Adapter(blocks)).RunAsync(LastRound, CancellationToken.None));

        Assert.Equal(3UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task An_Invalid_Block_That_Overdraws_An_Account_Is_Not_Published()
    {
        var storage = await CreateStorageAsync();
        var chain = new TestChain();
        var blocks = Blocks(chain).ToArray();
        blocks[1] = chain.Block(2, [new TestPayment(Carol, Alice, 1_000_000)], rewards: RewardsAt(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateIndexer(storage, Adapter(blocks)).RunAsync(5, CancellationToken.None));

        Assert.Equal(1UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        Assert.True((await storage.GetBalanceAtRoundAsync(Carol, Native, 1, CancellationToken.None)).IsZero);
    }

    [Fact]
    public async Task Genesis_Of_Another_Network_Is_Refused_Before_Anything_Is_Written()
    {
        var storage = await CreateStorageAsync();
        var adapter = new FakeChainAdapter(Chain.Profile, Genesis with { Network = "mainnet" });

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateIndexer(storage, adapter).RunAsync(1, CancellationToken.None));

        Assert.Null(await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Fact]
    public async Task Indexing_Stops_On_Cancellation_Leaving_A_Consistent_Cursor()
    {
        var storage = await CreateStorageAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateIndexer(storage, Adapter()).RunAsync(LastRound, cts.Token));
    }
}
