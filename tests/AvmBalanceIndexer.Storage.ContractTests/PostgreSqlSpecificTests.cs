using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.TestSupport;
using Npgsql;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>Behaviour that only exists because the PostgreSQL provider is transactional and partitioned.</summary>
[Collection(PostgresServerDefinition.Name)]
public class PostgreSqlSpecificTests(PostgresFixture postgres)
{
    private static readonly TokenKey Native = TokenKey.Native;

    [Fact]
    public async Task A_Failing_Round_Is_Rolled_Back_Completely_And_Not_Published()
    {
        var storage = await postgres.CreateStorageAsync();
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [Effect(0, 0, "genesis", TestChain.Account(1), 100, 100)],
            CancellationToken.None);

        // PostgreSQL rejects NUL characters in text, which fails the statement after the first table was written.
        var poisoned = new[]
        {
            Effect(1, 0, "fine", TestChain.Account(1), -10, 90),
            Effect(1, 1, "bad\0id", TestChain.Account(2), 10, 10),
        };

        await Assert.ThrowsAnyAsync<Exception>(() => storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 1, TestChain.FakeHash(1), null, IngestionState.Following, DateTimeOffset.UnixEpoch),
            poisoned,
            CancellationToken.None));

        var status = await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);
        Assert.Equal(0UL, status!.IndexedRound);
        var history = await storage.GetEffectsAsync(TestChain.Account(1), Native, null, null, null, 10, CancellationToken.None);
        Assert.Equal(0UL, Assert.Single(history.Items).Id.Round);
        var holding = await storage.GetHoldingsAsync(TestChain.Account(1), false, null, 10, CancellationToken.None);
        Assert.Equal(100UL, (ulong)Assert.Single(holding.Items).Amount.Value);
        Assert.Empty((await storage.GetHoldingsAsync(TestChain.Account(2), true, null, 10, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task A_Database_Without_A_Schema_Reports_Nothing_Indexed_Instead_Of_Failing()
    {
        var storage = await postgres.CreateStorageAsync(applySchema: false);

        Assert.Null(await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Fact]
    public async Task Token_Kinds_Sort_Bytewise_Whatever_The_Database_Collation()
    {
        var storage = await postgres.CreateStorageAsync();
        var account = TestChain.Account(1);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [
                Effect(0, 0, "a", account, 5, 5),
                new TokenEffect(new EffectId(0, 0, "b", [], 1), account, TokenKey.Asa(7), EffectType.Genesis, 6, Amount.From(6), null),
                new TokenEffect(new EffectId(0, 0, "c", [], 2), account, TokenKey.Arc200(3), EffectType.Genesis, 7, Amount.From(7), null),
            ],
            CancellationToken.None);

        var first = await storage.GetHoldingsAsync(account, true, null, 2, CancellationToken.None);
        var second = await storage.GetHoldingsAsync(account, true, first.NextCursor, 2, CancellationToken.None);

        Assert.Equal(["algo:0", "arc200:3"], first.Items.Select(static h => h.Token.ToString()));
        Assert.Equal(["asa:7"], second.Items.Select(static h => h.Token.ToString()));
    }

    [Fact]
    public async Task Two_Writers_Bootstrapping_At_Once_Cannot_Both_Win_With_Different_Chains()
    {
        var storage = await postgres.CreateStorageAsync();
        var account = TestChain.Account(1);

        Task Bootstrap(byte[] hash) => storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, hash, null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [Effect(0, 0, "genesis", account, 100, 100)],
            CancellationToken.None);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(async i =>
            {
                try
                {
                    await Bootstrap(TestChain.FakeHash((ulong)(i % 2)));
                    return true;
                }
                catch (ChainDivergenceException)
                {
                    return false;
                }
            }));

        // Whichever hash wins the lock, every writer with the other hash is refused instead of overwriting the cursor.
        Assert.Equal(3, results.Count(static r => r));
        var status = await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);
        Assert.Equal(0UL, status!.IndexedRound);
        Assert.Single((await storage.GetEffectsAsync(account, Native, null, null, null, 10, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task A_Very_Large_Round_Is_Written_In_Bounded_Batches()
    {
        var storage = await postgres.CreateStorageAsync();
        const int accounts = 12_345;
        var effects = Enumerable.Range(0, accounts)
            .Select(i => Effect(0, i, "genesis", TestChain.Account(1000 + i), 7, 7))
            .ToArray();

        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            effects,
            CancellationToken.None);

        var holders = 0;
        string? cursor = null;
        do
        {
            var page = await storage.GetHoldersAtRoundAsync(Native, 0, false, cursor, 1000, CancellationToken.None);
            holders += page.Items.Count;
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(accounts, holders);
    }

    [Fact]
    public async Task Changing_The_Partition_Count_Of_An_Existing_Database_Fails_With_A_Clear_Message()
    {
        var original = await postgres.CreateStorageAsync(accountPartitions: 4);
        var changed = new AvmBalanceIndexer.Storage.PostgreSql.PostgreSqlChainStorage(
            postgres.DataSourceOf(original),
            new ChainContext(ChainId.AlgorandTestnet),
            8);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => changed.EnsureSchemaAsync(CancellationToken.None));

        Assert.Contains("4 account hash partitions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_Ids_That_Do_Not_Fit_The_Signed_Column_Are_Refused_Instead_Of_Misordered()
    {
        var storage = await postgres.CreateStorageAsync();
        var account = TestChain.Account(1);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [Effect(0, 0, "genesis", account, 5, 5)],
            CancellationToken.None);
        var huge = new TokenEffect(new EffectId(1, 1, "tx", [], 0), account, TokenKey.Asa(ulong.MaxValue), EffectType.AssetTransfer, 1, Amount.From(1), null);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 1, TestChain.FakeHash(1), null, IngestionState.Following, DateTimeOffset.UnixEpoch),
            [huge],
            CancellationToken.None));
        Assert.Equal(0UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task Reading_Before_The_Schema_Exists_Reports_That_Nothing_Is_Indexed()
    {
        var storage = await postgres.CreateStorageAsync(applySchema: false);

        await Assert.ThrowsAsync<ChainNotStartedException>(() => storage.GetBalanceAtRoundAsync(TestChain.Account(1), Native, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ChainNotStartedException>(() => storage.GetHoldingsAsync(TestChain.Account(1), true, null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task An_Unknown_Token_Kind_In_The_Database_Is_An_Error_Not_A_Wrong_Token()
    {
        var storage = await postgres.CreateStorageAsync();
        var account = TestChain.Account(1);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [Effect(0, 0, "genesis", account, 5, 5)],
            CancellationToken.None);
        await using (var connection = await postgres.DataSourceOf(storage).OpenConnectionAsync())
        await using (var command = new Npgsql.NpgsqlCommand("UPDATE holdings_by_account SET token_kind = 'futurekind'", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => storage.GetHoldingsAsync(account, true, null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Schema_Bootstrap_Is_Idempotent()
    {
        var storage = await postgres.CreateStorageAsync();

        await storage.EnsureSchemaAsync(CancellationToken.None);
        await storage.EnsureSchemaAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Concurrent_Schema_Bootstraps_Do_Not_Collide()
    {
        var storage = await postgres.CreateStorageAsync();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => storage.EnsureSchemaAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task Rounds_In_Different_Buckets_Land_In_Their_Own_Partitions()
    {
        var storage = await postgres.CreateStorageAsync();
        var account = TestChain.Account(1);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [Effect(0, 0, "genesis", account, 100, 100)],
            CancellationToken.None);

        var history = await storage.GetEffectsAsync(account, Native, null, null, null, 10, CancellationToken.None);

        Assert.Single(history.Items);
        Assert.Equal(0, RoundBuckets.ForRound(99_999));
        Assert.Equal(1, RoundBuckets.ForRound(100_000));
    }

    [Fact]
    public async Task A_Database_Created_Before_The_Token_Index_Existed_Is_Backfilled_On_Schema_Apply()
    {
        var storage = await postgres.CreateStorageAsync();
        var account = TestChain.Account(1);
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [Effect(0, 0, "a", account, 5, 5)],
            CancellationToken.None);
        await using (var command = postgres.DataSourceOf(storage).CreateCommand("DROP TABLE holdings_by_token"))
        {
            await command.ExecuteNonQueryAsync();
        }

        await storage.EnsureSchemaAsync(CancellationToken.None);

        var holders = await storage.GetHoldersAtRoundAsync(Native, 0, false, null, 10, CancellationToken.None);
        Assert.Equal(5UL, (ulong)Assert.Single(holders.Items).Amount.Value);
    }

    [Fact]
    public async Task A_Read_That_Exceeds_The_Timeout_Is_A_Storage_Timeout_Not_A_Hang()
    {
        var storage = await postgres.CreateStorageAsync(readTimeout: TimeSpan.FromMilliseconds(300));
        await storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch),
            [Effect(0, 0, "a", TestChain.Account(1), 5, 5)],
            CancellationToken.None);

        // A writer holding the table makes the holder scan wait, which is what a scan that is too large looks like to the client.
        await using var blocker = await postgres.DataSourceOf(storage).OpenConnectionAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("LOCK TABLE holdings_by_token IN ACCESS EXCLUSIVE MODE", blocker, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<StorageTimeoutException>(() => storage.GetHoldersAtRoundAsync(Native, 0, false, null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task A_Storage_Bound_To_One_Chain_Rejects_Another()
    {
        var storage = await postgres.CreateStorageAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetAsync(ChainId.Voi, CancellationToken.None));
    }

    [Fact]
    public void The_Read_Timeout_Must_Be_Positive()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=localhost");

        Assert.Throws<ArgumentOutOfRangeException>(() => new AvmBalanceIndexer.Storage.PostgreSql.PostgreSqlChainStorage(dataSource, new ChainContext(ChainId.Voi), 4, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvmBalanceIndexer.Storage.PostgreSql.PostgreSqlChainStorage(dataSource, new ChainContext(ChainId.Voi), 4, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Account_Partition_Count_Is_Validated()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=localhost");

        Assert.Throws<ArgumentOutOfRangeException>(() => new AvmBalanceIndexer.Storage.PostgreSql.PostgreSqlChainStorage(dataSource, new ChainContext(ChainId.Voi), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvmBalanceIndexer.Storage.PostgreSql.PostgreSqlChainStorage(dataSource, new ChainContext(ChainId.Voi), 5000));
    }

    private static TokenEffect Effect(ulong round, int ordinal, string transactionId, Address account, long delta, ulong after) =>
        new(
            new EffectId(round, 0, transactionId, [], ordinal),
            account,
            Native,
            round == 0 ? EffectType.Genesis : EffectType.Payment,
            delta,
            Amount.From(after),
            null);
}
