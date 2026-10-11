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
    public async Task Progress_Updates_Change_Target_State_And_Time_But_Never_The_Cursor()
    {
        var storage = await IndexedAsync();
        var published = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!;
        var later = DateTimeOffset.UnixEpoch.AddYears(30);

        await storage.UpdateProgressAsync(published with { TargetRound = 99, State = IngestionState.Following, UpdatedAt = later }, CancellationToken.None);
        await storage.UpdateProgressAsync(published with { IndexedRound = 2, TargetRound = 1, State = IngestionState.Failed }, CancellationToken.None);

        var status = (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!;
        Assert.Equal((LastRound, 99UL, IngestionState.Following, later), (status.IndexedRound, status.TargetRound, status.State, status.UpdatedAt));
        Assert.Equal(published.IndexedBlockHash.ToArray(), status.IndexedBlockHash.ToArray());
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

    [Theory]
    [InlineData("zz")]
    [InlineData("algo")]
    [InlineData("algo:x")]
    [InlineData("algo:-1")]
    [InlineData("bogus:1")]
    [InlineData("algo\0:1")]
    public async Task A_Malformed_Holdings_Cursor_Is_An_Argument_Error(string cursor)
    {
        var storage = await IndexedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetHoldingsAsync(Alice, true, cursor, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetHoldingsAsync(TestChain.Account(99), true, cursor, 10, CancellationToken.None));
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("1:2")]
    [InlineData("1:2:x")]
    [InlineData("1:-2:3")]
    [InlineData("9223372036854775808:0:0")]
    public async Task A_Malformed_Effects_Cursor_Is_An_Argument_Error(string cursor)
    {
        var storage = await IndexedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetEffectsAsync(Alice, Native, null, null, cursor, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetEffectsAsync(TestChain.Account(99), Native, null, null, cursor, 10, CancellationToken.None));
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
    public async Task Holders_Report_The_Opt_In_State_Of_The_Round_They_Are_Read_At()
    {
        var storage = await CreateStorageAsync();
        var asset = TokenKey.Asa(7);
        var holder = TestChain.Account(50);
        var other = TestChain.Account(51);
        TokenEffect AssetEffect(ulong round, int ordinal, Address who, EffectType type, long delta, ulong after) =>
            new(new EffectId(round, 1, $"TX{round}", [], ordinal), who, asset, type, delta, Amount.From(after), null);

        // round 1: both opt in; round 2: holder receives 5; round 3: holder returns them and opts out; round 4: opts in again.
        await storage.WriteRoundAsync(new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch), [], CancellationToken.None);
        await storage.WriteRoundAsync(
            Status(1),
            [AssetEffect(1, 0, holder, EffectType.AssetOptIn, 0, 0), AssetEffect(1, 1, other, EffectType.AssetOptIn, 0, 0)],
            CancellationToken.None);
        await storage.WriteRoundAsync(Status(2), [AssetEffect(2, 0, holder, EffectType.AssetTransfer, 5, 5)], CancellationToken.None);
        await storage.WriteRoundAsync(Status(3), [AssetEffect(3, 0, holder, EffectType.AssetOptOut, -5, 0)], CancellationToken.None);
        var optedOutNow = await storage.GetHoldersAtRoundAsync(asset, 3, true, null, 10, CancellationToken.None);
        Assert.False(optedOutNow.Items.Single(h => h.Account == holder).OptedIn);
        await storage.WriteRoundAsync(Status(4), [AssetEffect(4, 0, holder, EffectType.AssetOptIn, 0, 0)], CancellationToken.None);

        foreach (var round in new[] { 0UL, 1UL, 2UL, 3UL, 4UL })
        {
            var holders = await storage.GetHoldersAtRoundAsync(asset, round, true, null, 10, CancellationToken.None);
            var byAccount = holders.Items.ToDictionary(static h => h.Account);
            if (round == 0)
            {
                Assert.Empty(byAccount);
                continue;
            }

            Assert.Equal(round != 3, byAccount[holder].OptedIn);
            Assert.True(byAccount[other].OptedIn);
        }

        var current = await storage.GetHoldingsAsync(holder, true, null, 10, CancellationToken.None);
        Assert.True(Assert.Single(current.Items, h => h.Token == asset).OptedIn);

        ChainStatus Status(ulong round) => new(ChainId.AlgorandTestnet, round, TestChain.FakeHash(round), null, IngestionState.Following, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task The_Writer_Lease_Is_Exclusive_And_Free_Again_After_It_Is_Released()
    {
        var provider = Assert.IsAssignableFrom<IWriterLeaseProvider>(await CreateStorageAsync());

        var first = await provider.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(first);
        Assert.False(first.Lost.IsCancellationRequested);
        Assert.Null(await provider.TryAcquireAsync(CancellationToken.None));

        await first.DisposeAsync();

        var second = await provider.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(second);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task Holders_At_The_Latest_Round_Match_The_Current_Holdings_With_And_Without_Zero_Balances()
    {
        var storage = await IndexedAsync();

        var withZero = new List<Holding>();
        string? cursor = null;
        do
        {
            var page = await storage.GetHoldersAtRoundAsync(Native, LastRound, true, cursor, 2, CancellationToken.None);
            withZero.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        var withoutZero = await storage.GetHoldersAtRoundAsync(Native, LastRound, false, null, 100, CancellationToken.None);

        Assert.Equal(6, withZero.Count);
        Assert.Contains(withZero, h => h.Account == Carol && h.Amount.IsZero);
        Assert.Equal(5, withoutZero.Items.Count);
        Assert.Equal(withZero.Select(static h => h.Account.ToArray()).Order(Comparer<byte[]>.Create(static (x, y) => x.AsSpan().SequenceCompareTo(y))), withZero.Select(static h => h.Account.ToArray()));
        foreach (var holder in withZero)
        {
            var current = Assert.Single((await storage.GetHoldingsAsync(holder.Account, true, null, 10, CancellationToken.None)).Items);
            Assert.Equal((current.Amount, current.LastChangedRound), (holder.Amount, holder.LastChangedRound));
            Assert.Equal(ExpectedBalance(holder.Account, LastRound), (ulong)holder.Amount.Value);
        }
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
    public async Task Two_Effects_With_The_Same_Identity_Fail_The_Round_Instead_Of_Being_Dropped()
    {
        var storage = await CreateStorageAsync();
        await CreateIndexer(storage, Adapter()).RunAsync(1, CancellationToken.None);
        var id = new EffectId(2, 1, "TX", [], 0);
        var effects = new[]
        {
            new TokenEffect(id, Alice, Native, EffectType.Payment, -5, Amount.From(1), null),
            new TokenEffect(id, Alice, Native, EffectType.Payment, -5, Amount.From(1), null),
        };
        var status = new ChainStatus(ChainId.AlgorandTestnet, 2, TestChain.FakeHash(2), null, IngestionState.Following, DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAnyAsync<Exception>(() => storage.WriteRoundAsync(status, effects, CancellationToken.None));

        Assert.Equal(1UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
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

    [Fact]
    public async Task An_Asset_Lifecycle_Keeps_Balances_Opt_Ins_And_The_Token_Record()
    {
        const ulong asset = 100;
        var key = TokenKey.Asa(asset);
        var chain = new TestChain();
        var storage = await CreateStorageAsync();
        var blocks = new[]
        {
            chain.Block(1, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(asset, 1_000, decimals: 2, unit: "TST", name: "Test asset"))], rewards: RewardsAt(1)),
            chain.Block(2, [new TestPayment(Bob, Bob, 0, Asset: TestAsset.OptIn(asset))], rewards: RewardsAt(2)),
            chain.Block(3, [new TestPayment(Alice, Bob, 0, Asset: TestAsset.Transfer(asset, 300))], rewards: RewardsAt(3)),
            chain.Block(4, [new TestPayment(Bob, Alice, 0, Asset: TestAsset.Transfer(asset, 50))], rewards: RewardsAt(4)),
            chain.Block(5, [new TestPayment(Bob, Bob, 0, Asset: TestAsset.CloseOut(asset, Alice, closingAmount: 250))], rewards: RewardsAt(5)),
            chain.Block(6, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Destroy(asset))], rewards: RewardsAt(6)),
        };

        await CreateIndexer(storage, Adapter(blocks)).RunAsync(6, CancellationToken.None);

        // The historical holders report OptedIn = true after an opt-out (issue #35), so only balances are checked here.
        async Task<ulong?> HolderAsync(Address account, ulong round)
        {
            var page = await storage.GetHoldersAtRoundAsync(key, round, true, null, 10, CancellationToken.None);
            return page.Items.Where(h => h.Account == account).Select(static h => (ulong?)(ulong)h.Amount.Value).SingleOrDefault();
        }

        Assert.Null(await HolderAsync(Alice, 0));
        Assert.Equal(1_000UL, await HolderAsync(Alice, 1));
        Assert.Equal(0UL, await HolderAsync(Bob, 2));
        Assert.Equal(700UL, await HolderAsync(Alice, 3));
        Assert.Equal(300UL, await HolderAsync(Bob, 3));
        Assert.Equal(250UL, await HolderAsync(Bob, 4));
        Assert.Equal(750UL, await HolderAsync(Alice, 4));
        Assert.Equal(1_000UL, await HolderAsync(Alice, 5));
        Assert.Equal(0UL, await HolderAsync(Bob, 5));
        Assert.Equal(0UL, await HolderAsync(Alice, 6));

        var current = Assert.Single((await storage.GetHoldingsAsync(Bob, true, null, 10, CancellationToken.None)).Items, h => h.Token == key);
        Assert.False(current.OptedIn);
        Assert.True(current.Amount.IsZero);
        Assert.True(Assert.Single((await storage.GetHoldingsAsync(Alice, true, null, 10, CancellationToken.None)).Items, h => h.Token == key).Amount.IsZero);
        Assert.Equal(1_000UL, (ulong)(await storage.GetBalanceAtRoundAsync(Alice, key, 5, CancellationToken.None)).Value);

        var token = await storage.GetAsync(key, CancellationToken.None);
        Assert.NotNull(token);
        Assert.Equal((Alice, "TST", "Test asset", 2, 1UL, 6UL), (token.Creator, token.Symbol, token.Name, token.Decimals, token.CreatedRound, token.DeletedRound));
    }

    [Fact]
    public async Task Arc200_Balances_Beyond_Uint64_Round_Trip_And_Unknown_Debits_Are_Limited_To_The_Balance()
    {
        const ulong application = 55;
        var key = TokenKey.Arc200(application);
        var supply = System.Numerics.BigInteger.Pow(10, 40);
        var chain = new TestChain();
        var storage = await CreateStorageAsync();
        var zero = new Address(new byte[Address.Length]);
        TestPayment Call(Address sender, params byte[][] logs) => new(sender, sender, 0, Application: new TestApplication(application, logs));
        var blocks = new[]
        {
            chain.Block(1, [Call(Alice, TestChain.Arc200TransferLog(zero, Alice, supply))], rewards: RewardsAt(1)),
            chain.Block(2, [Call(Alice, TestChain.Arc200TransferLog(Alice, Bob, supply / 4))], rewards: RewardsAt(2)),
            chain.Block(3, [Call(Bob, TestChain.Arc200TransferLog(Bob, zero, 5), TestChain.Arc200TransferLog(Bob, Alice, supply))], rewards: RewardsAt(3)),
        };

        await CreateIndexer(storage, Adapter(blocks)).RunAsync(3, CancellationToken.None);

        var bob = supply / 4;
        Assert.Equal(supply, (await storage.GetBalanceAtRoundAsync(Alice, key, 1, CancellationToken.None)).Value);
        Assert.Equal(supply - bob, (await storage.GetBalanceAtRoundAsync(Alice, key, 2, CancellationToken.None)).Value);
        Assert.Equal(bob, (await storage.GetBalanceAtRoundAsync(Bob, key, 2, CancellationToken.None)).Value);

        // Bob burns 5 and then tries to send more than he holds: only what he held is moved, so the supply stays consistent.
        Assert.True((await storage.GetBalanceAtRoundAsync(Bob, key, 3, CancellationToken.None)).IsZero);
        Assert.Equal(supply - 5, (await storage.GetBalanceAtRoundAsync(Alice, key, 3, CancellationToken.None)).Value);

        var holders = await storage.GetHoldersAtRoundAsync(key, 3, false, null, 10, CancellationToken.None);
        var holder = Assert.Single(holders.Items);
        Assert.Equal((Alice, supply - 5), (holder.Account, holder.Amount.Value));
        Assert.Equal(supply - 5, (await storage.GetHoldingsAsync(Alice, false, null, 10, CancellationToken.None)).Items.Single(h => h.Token == key).Amount.Value);

        var token = await storage.GetAsync(key, CancellationToken.None);
        Assert.NotNull(token);
        Assert.Equal((TokenStandard.Arc200, false, 1UL), (token.Standard, token.StandardVerified, token.CreatedRound));
    }

    [Fact]
    public async Task A_Smart_Asa_Is_Classified_And_Reclassified_By_A_Later_Reconfiguration()
    {
        const ulong asset = 300;
        const ulong application = 88;
        var key = TokenKey.Asa(asset);
        var chain = new TestChain();
        var storage = await CreateStorageAsync();
        var appAccount = ApplicationAddresses.Of(application);
        var creation = TestAsset.Creation(asset, 500, unit: "S-ASA", name: "SMART-ASA") with { DefaultFrozen = true, ClawbackAddress = appAccount, Manager = appAccount };
        var blocks = new[]
        {
            chain.Block(1, [new TestPayment(Alice, Bob, 0, Application: new TestApplication(application), Inner: [new TestPayment(appAccount, appAccount, 0, Fee: 0, Asset: creation)])], rewards: RewardsAt(1)),
            chain.Block(2, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Reconfiguration(asset, Alice, null))], rewards: RewardsAt(2)),
        };
        var indexer = CreateIndexer(storage, Adapter(blocks));

        await indexer.RunAsync(1, CancellationToken.None);
        var smart = await storage.GetAsync(key, CancellationToken.None);

        Assert.NotNull(smart);
        Assert.Equal((TokenStandard.Arc20, false, (ulong?)application, appAccount), (smart.Standard, smart.StandardVerified, smart.ControllingAppId, smart.Creator));
        Assert.Equal(500UL, (ulong)(await storage.GetBalanceAtRoundAsync(appAccount, key, 1, CancellationToken.None)).Value);

        await indexer.RunAsync(2, CancellationToken.None);
        var plain = await storage.GetAsync(key, CancellationToken.None);

        Assert.NotNull(plain);
        Assert.Equal((TokenStandard.Asa, (ulong?)null, appAccount), (plain.Standard, plain.ControllingAppId, plain.Creator));
    }
}

