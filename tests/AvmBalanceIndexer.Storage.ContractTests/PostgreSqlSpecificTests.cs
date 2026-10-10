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
    public async Task A_Storage_Bound_To_One_Chain_Rejects_Another()
    {
        var storage = await postgres.CreateStorageAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetAsync(ChainId.Voi, CancellationToken.None));
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
