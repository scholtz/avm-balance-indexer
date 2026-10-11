using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.TestSupport;
using Cassandra;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>Behaviour that only exists because Cassandra has no cross-partition transaction.</summary>
[Collection(CassandraServerDefinition.Name)]
public class CassandraSpecificTests(CassandraFixture cassandra)
{
    private static readonly TokenKey Native = TokenKey.Native;

    private static ChainStatus Status(ulong round, IngestionState state = IngestionState.Following) =>
        new(ChainId.AlgorandTestnet, round, TestChain.FakeHash(round), null, state, DateTimeOffset.UnixEpoch);

    private static TokenEffect Effect(ulong round, int ordinal, Address account, long delta, ulong after, EffectType type = EffectType.Payment) =>
        new(new EffectId(round, ordinal + 1, round == 0 ? string.Empty : $"TX{round}", [], ordinal), account, Native, type, new BigInteger(delta), Amount.From(after), null);

    [Fact]
    public async Task A_Keyspace_Without_Schema_Reports_Nothing_Indexed_Instead_Of_Failing()
    {
        var storage = await cassandra.CreateStorageAsync(applySchema: false);

        Assert.Null(await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
        await Assert.ThrowsAsync<ChainNotStartedException>(() => storage.GetBalanceAtRoundAsync(TestChain.Account(1), Native, 0, CancellationToken.None));
        Assert.Empty(await storage.GetCurrentHoldingsAsync([new HoldingKey(TestChain.Account(1), Native)], CancellationToken.None));
    }

    [Fact]
    public async Task Applying_The_Schema_Twice_Is_Harmless()
    {
        var storage = await cassandra.CreateStorageAsync();

        await storage.EnsureSchemaAsync(CancellationToken.None);

        Assert.Null(await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Fact]
    public async Task Holdings_Written_By_A_Round_That_Was_Never_Published_Are_Not_Visible()
    {
        var storage = await cassandra.CreateStorageAsync();
        var account = TestChain.Account(1);
        var ghost = TestChain.Account(2);
        await storage.WriteRoundAsync(Status(0, IngestionState.Bootstrapping), [Effect(0, 0, account, 100, 100, EffectType.Genesis)], CancellationToken.None);

        // What a writer that crashed after the holdings but before publishing round 1 leaves behind.
        var session = await cassandra.SessionOfAsync(storage);
        var keyspace = cassandra.KeyspaceOf(storage);
        foreach (var (who, amount) in new[] { (account, "999"), (ghost, "5") })
        {
            await session.ExecuteAsync(new SimpleStatement(
                $"INSERT INTO {keyspace}.holdings_by_account (account, token_kind, token_id, amount, opted_in, last_changed_round) VALUES (?, 'algo', 0, '{amount}', true, 1) USING TIMESTAMP 2",
                who.ToArray()));
        }

        var current = await storage.GetCurrentHoldingsAsync([new HoldingKey(account, Native), new HoldingKey(ghost, Native)], CancellationToken.None);
        var listed = await storage.GetHoldingsAsync(account, false, null, 10, CancellationToken.None);

        Assert.Equal(100UL, (ulong)current[new HoldingKey(account, Native)].Amount.Value);
        Assert.Equal(0UL, current[new HoldingKey(account, Native)].LastChangedRound);
        Assert.DoesNotContain(new HoldingKey(ghost, Native), current.Keys);
        Assert.Equal(100UL, (ulong)Assert.Single(listed.Items).Amount.Value);
        Assert.Empty((await storage.GetHoldingsAsync(ghost, true, null, 10, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Latest_Holders_Ignore_Token_Index_Rows_Of_A_Round_That_Was_Never_Published()
    {
        var storage = await cassandra.CreateStorageAsync();
        var account = TestChain.Account(1);
        var ghost = TestChain.Account(2);
        await storage.WriteRoundAsync(Status(0, IngestionState.Bootstrapping), [Effect(0, 0, account, 100, 100, EffectType.Genesis)], CancellationToken.None);

        var session = await cassandra.SessionOfAsync(storage);
        var keyspace = cassandra.KeyspaceOf(storage);
        foreach (var (who, amount) in new[] { (account, "999"), (ghost, "5") })
        {
            await session.ExecuteAsync(new SimpleStatement(
                $"INSERT INTO {keyspace}.holdings_by_token (token_kind, token_id, shard, account, amount, last_changed_round) VALUES ('algo', 0, {AvmBalanceIndexer.Storage.Cassandra.CassandraChainStorage.ShardOf(who)}, ?, '{amount}', 1) USING TIMESTAMP 2",
                who.ToArray()));
        }

        var holders = await storage.GetHoldersAtRoundAsync(Native, 0, true, null, 10, CancellationToken.None);

        var holder = Assert.Single(holders.Items);
        Assert.Equal((account, 100UL, 0UL), (holder.Account, (ulong)holder.Amount.Value, holder.LastChangedRound));
    }

    [Fact]
    public async Task Driver_Timeouts_Become_Storage_Timeouts()
    {
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 9042);

        await Assert.ThrowsAsync<StorageTimeoutException>(() =>
            AvmBalanceIndexer.Storage.Cassandra.CassandraChainStorage.TranslateTimeoutsAsync<int>(() => throw new OperationTimedOutException(endpoint, 1000)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AvmBalanceIndexer.Storage.Cassandra.CassandraChainStorage.TranslateTimeoutsAsync<int>(() => throw new InvalidOperationException()));
    }

    [Fact]
    public async Task Replaying_A_Round_Whose_Rows_Were_Partly_Written_Converges_To_The_Same_State()
    {
        var storage = await cassandra.CreateStorageAsync();
        var alice = TestChain.Account(1);
        var bob = TestChain.Account(2);
        await storage.WriteRoundAsync(Status(0, IngestionState.Bootstrapping), [Effect(0, 0, alice, 100, 100, EffectType.Genesis)], CancellationToken.None);

        // A crashed attempt: an effect row of round 1 exists but the cursor never moved.
        var session = await cassandra.SessionOfAsync(storage);
        var keyspace = cassandra.KeyspaceOf(storage);
        await session.ExecuteAsync(new SimpleStatement(
            $"INSERT INTO {keyspace}.effect_buckets_by_account_token (account, token_kind, token_id, bucket) VALUES (?, 'algo', 0, 0)",
            alice.ToArray()));
        await session.ExecuteAsync(new SimpleStatement(
            $"INSERT INTO {keyspace}.effects_by_account_token_bucket (account, token_kind, token_id, bucket, round, intra_round_order, ordinal, transaction_id, effect_type, delta, balance_after) VALUES (?, 'algo', 0, 0, 1, 1, 0, 'TX1', 'Payment', '-40', '60')",
            alice.ToArray()));
        Assert.Equal(100UL, (ulong)(await storage.GetBalanceAtRoundAsync(alice, Native, 0, CancellationToken.None)).Value);

        Assert.True(await storage.WriteRoundAsync(Status(1), [Effect(1, 0, alice, -40, 60), Effect(1, 1, bob, 40, 40)], CancellationToken.None));

        Assert.Equal(60UL, (ulong)(await storage.GetBalanceAtRoundAsync(alice, Native, 1, CancellationToken.None)).Value);
        Assert.Equal(40UL, (ulong)(await storage.GetBalanceAtRoundAsync(bob, Native, 1, CancellationToken.None)).Value);
        var history = await storage.GetEffectsAsync(alice, Native, null, null, null, 10, CancellationToken.None);
        Assert.Equal([1UL, 0UL], history.Items.Select(static e => e.Id.Round));
        Assert.Equal(2, (await storage.GetHoldersAtRoundAsync(Native, 1, false, null, 10, CancellationToken.None)).Items.Count);
    }

    [Fact]
    public async Task Of_Several_Writers_Racing_On_One_Round_Exactly_One_Publishes()
    {
        var storage = await cassandra.CreateStorageAsync();
        var alice = TestChain.Account(1);
        await storage.WriteRoundAsync(Status(0, IngestionState.Bootstrapping), [Effect(0, 0, alice, 100, 100, EffectType.Genesis)], CancellationToken.None);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                return await storage.WriteRoundAsync(Status(1), [Effect(1, 0, alice, -1, 99)], CancellationToken.None);
            }
            catch (ChainDivergenceException)
            {
                return false;
            }
        }));

        // Losers either see the round already published (no-op) or lose the compare-and-set; at most one applied it.
        Assert.Equal(1, results.Count(static r => r));
        Assert.Equal(1UL, (await storage.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
        Assert.Equal(99UL, (ulong)(await storage.GetBalanceAtRoundAsync(alice, Native, 1, CancellationToken.None)).Value);
    }

    [Fact]
    public async Task A_Genesis_With_Hundreds_Of_Accounts_Is_Written_And_Paged_Across_All_Token_Shards()
    {
        var storage = await cassandra.CreateStorageAsync();
        const int count = 300;
        var accounts = Enumerable.Range(100, count).Select(TestChain.Account).ToArray();
        var effects = accounts.Select((a, i) => Effect(0, i, a, i + 1, (ulong)i + 1, EffectType.Genesis)).ToArray();
        await storage.WriteRoundAsync(Status(0, IngestionState.Bootstrapping), effects, CancellationToken.None);

        var collected = new List<Holding>();
        string? cursor = null;
        do
        {
            var page = await storage.GetHoldersAtRoundAsync(Native, 0, false, cursor, 37, CancellationToken.None);
            collected.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        var expectedOrder = accounts.Select(static a => a.ToArray()).OrderBy(static b => b, Comparer<byte[]>.Create(static (x, y) => x.AsSpan().SequenceCompareTo(y))).ToArray();
        Assert.Equal(count, collected.Count);
        Assert.Equal(expectedOrder, collected.Select(static h => h.Account.ToArray()));
        Assert.Equal((ulong)count * (count + 1) / 2, collected.Aggregate(0UL, static (sum, h) => sum + (ulong)h.Amount.Value));
    }

    [Fact]
    public async Task Balances_And_History_Span_Round_Buckets()
    {
        var storage = await cassandra.CreateStorageAsync();
        var alice = TestChain.Account(1);
        var far = RoundBuckets.Size * 5 + 10;
        await storage.WriteRoundAsync(Status(0, IngestionState.Bootstrapping), [Effect(0, 0, alice, 100, 100, EffectType.Genesis)], CancellationToken.None);

        // Rounds in between are plain publications without effects; plant the far cursor and a far effect directly instead
        // of writing half a million empty rounds.
        var session = await cassandra.SessionOfAsync(storage);
        var keyspace = cassandra.KeyspaceOf(storage);
        await session.ExecuteAsync(new SimpleStatement(
            $"INSERT INTO {keyspace}.effect_buckets_by_account_token (account, token_kind, token_id, bucket) VALUES (?, 'algo', 0, 5)",
            alice.ToArray()));
        await session.ExecuteAsync(new SimpleStatement(
            $"INSERT INTO {keyspace}.effects_by_account_token_bucket (account, token_kind, token_id, bucket, round, intra_round_order, ordinal, transaction_id, effect_type, delta, balance_after) VALUES (?, 'algo', 0, 5, {far}, 1, 0, 'TXFAR', 'Payment', '-30', '70')",
            alice.ToArray()));
        await session.ExecuteAsync(new SimpleStatement(
            $"UPDATE {keyspace}.chain_status SET indexed_round = {far + 5} WHERE chain = '{ChainId.AlgorandTestnet}' IF indexed_round = 0"));

        Assert.Equal(100UL, (ulong)(await storage.GetBalanceAtRoundAsync(alice, Native, far - 1, CancellationToken.None)).Value);
        Assert.Equal(70UL, (ulong)(await storage.GetBalanceAtRoundAsync(alice, Native, far, CancellationToken.None)).Value);
        var all = await storage.GetEffectsAsync(alice, Native, null, null, null, 1, CancellationToken.None);
        var rest = await storage.GetEffectsAsync(alice, Native, null, null, all.NextCursor, 10, CancellationToken.None);
        Assert.Equal(far, Assert.Single(all.Items).Id.Round);
        Assert.Equal([0UL], rest.Items.Select(static e => e.Id.Round));
        var onlyOld = await storage.GetEffectsAsync(alice, Native, null, far - 1, null, 10, CancellationToken.None);
        Assert.Equal([0UL], onlyOld.Items.Select(static e => e.Id.Round));
        var onlyNew = await storage.GetEffectsAsync(alice, Native, 1, null, null, 10, CancellationToken.None);
        Assert.Equal([far], onlyNew.Items.Select(static e => e.Id.Round));
    }
}
