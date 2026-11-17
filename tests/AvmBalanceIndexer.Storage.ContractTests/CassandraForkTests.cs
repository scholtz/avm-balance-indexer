using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.Storage.Cassandra;
using AvmBalanceIndexer.TestSupport;
using Cassandra;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.RebuildAssertions;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>The partial rebuild of the Cassandra provider (ADR 0021): a new keyspace is filled from an earlier one up to a round.</summary>
[Collection(CassandraServerDefinition.Name)]
public class CassandraForkTests(CassandraFixture cassandra)
{
    private static readonly TokenKey Native = TokenKey.Native;

    private static RoundIndexer Indexer(IChainStorage storage) =>
        new(
            Scenario.Adapter(),
            new AlgoEffectNormalizer(),
            storage,
            Options.Create(new IndexerOptions { FetchConcurrency = 3, BalanceCacheEntries = 1_000 }),
            NullLogger<RoundIndexer>.Instance);

    private async Task<CassandraChainStorage> IndexedAsync(ulong upTo)
    {
        var storage = await cassandra.CreateStorageAsync();
        await Indexer(storage).RunAsync(upTo, CancellationToken.None);
        return storage;
    }

    private Task<CassandraChainStorage> TargetAsync(CassandraChainStorage source, ulong round) =>
        cassandra.CreateStorageAsync(forkFrom: (cassandra.KeyspaceOf(source), round));

    private static Task<PartialRebuildResult> ForkAsync(CassandraChainStorage target, byte[] hash, IProgress<PartialRebuildProgress>? progress = null, CancellationToken cancellationToken = default) =>
        target.RunAsync(hash, progress, cancellationToken);

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public async Task A_Fork_Equals_A_Keyspace_Indexed_Straight_To_The_Round(ulong round)
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(round);
        var target = await TargetAsync(full, round);

        var result = await ForkAsync(target, await HashOfAsync(reference, round));

        Assert.Equal(PartialRebuildOutcome.Done, result.Outcome);
        Assert.True(result.EffectsCopied > 0);
        await AssertSameAsync(target, reference, round);
        Assert.Equal(IngestionState.Backfilling, (await target.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task Indexing_Continues_After_The_Fork_To_The_Same_Result_As_The_Source()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(2);
        var target = await TargetAsync(full, 2);
        await ForkAsync(target, await HashOfAsync(reference, 2));

        // The first round after the fork checks its previous hash against the stored one.
        await Indexer(target).RunAsync(Scenario.LastRound, CancellationToken.None);

        await AssertSameAsync(target, full, Scenario.LastRound);
    }

    [Fact]
    public async Task Tokens_Created_After_The_Round_Are_Not_Taken_And_A_Later_Destruction_Is_Undone()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        await full.UpsertAsync(new TokenInfo(TokenKey.Asa(7), null, 0, "OLD", "Old", TokenStandard.Asa, true, null, 2, 5, null), CancellationToken.None);
        await full.UpsertAsync(new TokenInfo(TokenKey.Asa(8), null, 0, "NEW", "New", TokenStandard.Asa, true, null, 4, null, null), CancellationToken.None);
        var target = await TargetAsync(full, 3);

        await ForkAsync(target, await HashOfAsync(reference, 3));

        var old = await target.GetAsync(TokenKey.Asa(7), CancellationToken.None);
        Assert.Equal((2UL, (ulong?)null), (old!.CreatedRound!.Value, old.DeletedRound));
        Assert.Null(await target.GetAsync(TokenKey.Asa(8), CancellationToken.None));
    }

    [Fact]
    public async Task A_Missing_Source_A_Source_Of_Another_Chain_And_A_Round_Beyond_The_Source_Are_Refused_Without_Publishing()
    {
        var full = await IndexedAsync(3);
        var hash = new byte[32];

        var missing = await cassandra.CreateStorageAsync(forkFrom: ("avm_no_such_keyspace", 1));
        var beyond = await TargetAsync(full, 4);
        var other = await cassandra.CreateStorageAsync(chain: ChainId.Voi);
        await other.WriteRoundAsync(new ChainStatus(ChainId.Voi, 0, TestChain.FakeHash(0), null, IngestionState.Bootstrapping, DateTimeOffset.UnixEpoch), [], CancellationToken.None);
        var wrongChain = await TargetAsync(other, 0);

        foreach (var target in new[] { missing, beyond, wrongChain })
        {
            var result = await ForkAsync(target, hash);
            Assert.Equal(PartialRebuildOutcome.Refused, result.Outcome);
            Assert.NotEmpty(result.Reason);
            Assert.Null(await target.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
        }
    }

    [Fact]
    public async Task A_Source_Keyspace_Of_An_Older_Layout_Is_Refused_Before_Anything_Is_Written()
    {
        var full = await IndexedAsync(3);
        var session = await cassandra.SessionOfAsync(full);
        var old = $"avm_old_layout_{Guid.NewGuid():N}"[..40];
        await session.ExecuteAsync(new SimpleStatement($"CREATE KEYSPACE {old} WITH replication = {{'class': 'SimpleStrategy', 'replication_factor': 1}}"));
        try
        {
            await session.ExecuteAsync(new SimpleStatement($"CREATE TABLE {old}.chain_status (chain text PRIMARY KEY, indexed_round bigint, indexed_block_hash blob)"));
            await session.ExecuteAsync(new SimpleStatement($"CREATE TABLE {old}.effects_by_account_token_bucket (account blob PRIMARY KEY)"));
            await session.ExecuteAsync(new SimpleStatement($"INSERT INTO {old}.chain_status (chain, indexed_round, indexed_block_hash) VALUES ('{ChainId.AlgorandTestnet}', 3, 0x00)"));
            var target = await cassandra.CreateStorageAsync(forkFrom: (old, 3));

            var result = await ForkAsync(target, new byte[32]);

            Assert.Equal(PartialRebuildOutcome.Refused, result.Outcome);
            Assert.Contains("missing", result.Reason, StringComparison.Ordinal);
            var rows = await (await cassandra.SessionOfAsync(target)).ExecuteAsync(new SimpleStatement($"SELECT stage FROM {cassandra.KeyspaceOf(target)}.fork_progress"));
            Assert.Empty(rows);
        }
        finally
        {
            await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE IF EXISTS {old}"));
        }
    }

    [Fact]
    public async Task A_Source_Holding_Another_Block_Hash_At_The_Round_Is_Refused()
    {
        var full = await IndexedAsync(3);
        var target = await TargetAsync(full, 3);

        var result = await ForkAsync(target, new byte[32]);

        Assert.Equal(PartialRebuildOutcome.Refused, result.Outcome);
        Assert.Null(await target.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Fact]
    public async Task A_Bucket_Without_Effects_Stops_The_Copy_Before_Anything_Is_Published()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        var target = await TargetAsync(full, 3);
        var session = await cassandra.SessionOfAsync(target);
        await session.ExecuteAsync(new SimpleStatement(
            $"INSERT INTO {cassandra.KeyspaceOf(target)}.effect_buckets_by_account_token (account, token_kind, token_id, bucket) VALUES (?, 'algo', 0, 7)",
            TestChain.Account(9).ToArray()));

        var hash = await HashOfAsync(reference, 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ForkAsync(target, hash));

        Assert.Null(await target.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Fact]
    public async Task A_Second_Run_Is_Already_Done_And_Changes_Nothing_And_The_Source_Is_Never_Touched()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        var hash = await HashOfAsync(reference, 3);
        var before = await CountsAsync(full);
        var target = await TargetAsync(full, 3);
        await ForkAsync(target, hash);
        var afterFork = await CountsAsync(target);

        var again = await ForkAsync(target, hash);

        Assert.Equal(PartialRebuildOutcome.AlreadyDone, again.Outcome);
        Assert.Equal(afterFork, await CountsAsync(target));
        Assert.Equal(before, await CountsAsync(full));
    }

    [Fact]
    public async Task A_Request_That_Was_Carried_Out_Does_Not_Read_The_Source_Which_May_Be_Retired_By_Then()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        var hash = await HashOfAsync(reference, 3);
        var target = await TargetAsync(full, 3);
        Assert.False(await target.IsCarriedOutAsync(CancellationToken.None));
        await ForkAsync(target, hash);
        Assert.True(await target.IsCarriedOutAsync(CancellationToken.None));
        Assert.False(await cassandra.WithFork(target, cassandra.KeyspaceOf(full), 2).IsCarriedOutAsync(CancellationToken.None));
        var session = await cassandra.SessionOfAsync(full);
        await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE {cassandra.KeyspaceOf(full)}"));

        var again = await ForkAsync(target, hash);

        Assert.Equal(PartialRebuildOutcome.AlreadyDone, again.Outcome);
        Assert.Null(await target.ReadSourceCursorAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_Copy_That_Stopped_Half_Way_Is_Resumed_Nothing_Is_Visible_Before_The_End_And_The_Result_Is_Equal()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        var hash = await HashOfAsync(reference, 3);
        var target = await TargetAsync(full, 3);
        using var cancel = new CancellationTokenSource();
        var reports = new CallbackProgress(report =>
        {
            // Before the cursor is published the keyspace is invisible to readers.
            Assert.Null(target.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None).GetAwaiter().GetResult());
            cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ForkAsync(target, hash, reports, cancel.Token));
        Assert.Null(await target.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));

        var result = await ForkAsync(target, hash);

        Assert.Equal(PartialRebuildOutcome.Done, result.Outcome);
        await AssertSameAsync(target, reference, 3);
    }

    [Fact]
    public async Task A_Keyspace_That_Holds_An_Unfinished_Copy_Of_Another_Round_Is_Refused()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(2);
        var target = await TargetAsync(full, 3);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ForkAsync(target, new byte[32], new CallbackProgress(_ => cancel.Cancel()), cancel.Token));

        var corrected = cassandra.WithFork(target, cassandra.KeyspaceOf(full), 2);
        var result = await ForkAsync(corrected, await HashOfAsync(reference, 2));

        Assert.Equal(PartialRebuildOutcome.Refused, result.Outcome);
        Assert.Contains("unfinished copy", result.Reason, StringComparison.Ordinal);
        Assert.Null(await target.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None));
    }

    [Fact]
    public async Task A_Keyspace_That_Follows_The_Chain_Without_A_Matching_Request_Is_Refused_Not_Already_Done()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var indexed = await IndexedAsync(2);
        var target = cassandra.WithFork(indexed, cassandra.KeyspaceOf(full), 2);

        var result = await ForkAsync(target, await HashOfAsync(indexed, 2));

        Assert.Equal(PartialRebuildOutcome.Refused, result.Outcome);
        Assert.Equal(2UL, (await indexed.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task Rows_Of_A_Round_That_Was_Never_Published_Are_Not_Taken_Over()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var ghost = TestChain.Account(9);
        var session = await cassandra.SessionOfAsync(full);
        var keyspace = cassandra.KeyspaceOf(full);
        var tip = Scenario.LastRound;

        // What a writer that crashed after the effects but before publishing the next round leaves behind.
        await session.ExecuteAsync(new SimpleStatement(
            $"INSERT INTO {keyspace}.effects_by_account_token_bucket (account, token_kind, token_id, bucket, round, intra_round_order, ordinal, transaction_id, effect_type, delta, balance_after) VALUES (?, 'algo', 0, 0, {tip + 1}, 1, 0, 'GHOST', 'Payment', '5', '5')",
            ghost.ToArray()));
        await session.ExecuteAsync(new SimpleStatement(
            $"INSERT INTO {keyspace}.accounts_touched_by_token (token_kind, token_id, shard, account, first_round) VALUES ('algo', 0, {CassandraChainStorage.ShardOf(ghost)}, ?, {tip + 1})",
            ghost.ToArray()));
        var target = await TargetAsync(full, tip);

        await ForkAsync(target, await HashOfAsync(full, tip));

        var targetSession = await cassandra.SessionOfAsync(target);
        var targetKeyspace = cassandra.KeyspaceOf(target);
        Assert.Empty(await targetSession.ExecuteAsync(new SimpleStatement($"SELECT round FROM {targetKeyspace}.effects_by_account_token_bucket WHERE account = ? AND token_kind = 'algo' AND token_id = 0 AND bucket = 0", ghost.ToArray())));
        Assert.Empty(await targetSession.ExecuteAsync(new SimpleStatement($"SELECT first_round FROM {targetKeyspace}.accounts_touched_by_token WHERE token_kind = 'algo' AND token_id = 0 AND shard = {CassandraChainStorage.ShardOf(ghost)} AND account = ?", ghost.ToArray())));
        Assert.Empty((await target.GetHoldingsAsync(ghost, true, null, 10, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Build_Records_Up_To_The_Round_Are_Copied()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        await full.AppendAsync(new IndexingTerm(0, "v1", at), CancellationToken.None);
        await full.AppendAsync(new IndexingTerm(4, "v2", at.AddDays(1)), CancellationToken.None);
        var target = await TargetAsync(full, 3);

        await ForkAsync(target, await HashOfAsync(reference, 3));

        Assert.Equal(["v1"], (await target.ListAsync(CancellationToken.None)).Select(static t => t.Version).ToArray());
    }

    [Theory]
    [InlineData("avm_x", "avm_x", true)] // the keyspace it fills
    [InlineData("system_schema", "avm_x", true)]
    [InlineData("bad name", "avm_x", true)]
    [InlineData("avm_old", "avm_x", false)]
    public void The_Source_Of_A_Fork_Must_Be_Another_Plain_Keyspace(string source, string keyspace, bool refused) =>
        Assert.Equal(refused, Errors(source, keyspace, [], "avm_x").Count > 0);

    [Fact]
    public void A_Source_That_Is_Also_Retired_And_A_Request_For_Another_Keyspace_Are_Refused()
    {
        Assert.NotEmpty(Errors("avm_old", "avm_x", ["avm_old"], "avm_x"));
        Assert.NotEmpty(Errors("avm_old", "avm_x", [], "avm_other"));
        Assert.Empty(Errors("avm_old", "avm_x", ["avm_older"], "avm_x"));
    }

    [Theory]
    [InlineData("avm_api", new[] { "avm_api" }, true)] // retiring the keyspace the API reads
    [InlineData("AVM_API", new[] { "avm_api" }, true)]
    [InlineData("avm_api", new[] { "avm_older" }, false)]
    [InlineData("bad name", new string[0], true)]
    [InlineData("", new[] { "avm_old" }, false)]
    public void The_Keyspace_The_Api_Reads_Can_Never_Be_Retired(string apiKeyspace, string[] retired, bool refused)
    {
        var services = new ServiceCollection();
        services.AddCassandraStorage();
        using var provider = services.BuildServiceProvider();
        var options = new StorageOptions { Provider = StorageProvider.Cassandra, Cassandra = { ContactPoints = ["localhost"], Keyspace = "avm_x", ApiKeyspace = apiKeyspace, RetiredKeyspaces = retired } };

        var result = provider.GetServices<IValidateOptions<StorageOptions>>().Single().Validate(null, options);

        Assert.Equal(refused, result.Failed);
    }

    private static List<string> Errors(string source, string keyspace, string[] retired, string into)
    {
        var services = new ServiceCollection();
        services.AddCassandraStorage();
        using var provider = services.BuildServiceProvider();
        var options = new StorageOptions
        {
            Provider = StorageProvider.Cassandra,
            Cassandra =
            {
                ContactPoints = ["localhost"],
                Keyspace = keyspace,
                RetiredKeyspaces = retired,
                ForkFrom = new CassandraForkOptions { Keyspace = source, Round = 5, Into = into },
            },
        };
        var result = provider.GetServices<IValidateOptions<StorageOptions>>().Single().Validate(null, options);
        return result.Failed ? [.. result.Failures!] : [];
    }

    private async Task<string> CountsAsync(CassandraChainStorage storage)
    {
        var session = await cassandra.SessionOfAsync(storage);
        var keyspace = cassandra.KeyspaceOf(storage);
        var parts = new List<string>();
        foreach (var table in new[] { "effects_by_account_token_bucket", "effects_by_token_bucket", "effect_buckets_by_account_token", "accounts_touched_by_token", "holdings_by_account", "holdings_by_token", "token_by_id", "chain_status" })
        {
            var count = (await session.ExecuteAsync(new SimpleStatement($"SELECT count(*) FROM {keyspace}.{table}"))).First().GetValue<long>(0);
            parts.Add($"{table}={count}");
        }

        return string.Join(';', parts);
    }

    private sealed class CallbackProgress(Action<PartialRebuildProgress> onReport) : IProgress<PartialRebuildProgress>
    {
        public void Report(PartialRebuildProgress value) => onReport(value);
    }
}
