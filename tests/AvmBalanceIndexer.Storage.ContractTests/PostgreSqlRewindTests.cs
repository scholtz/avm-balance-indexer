using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.Storage.PostgreSql;
using AvmBalanceIndexer.TestSupport;
using static AvmBalanceIndexer.TestSupport.RebuildAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>The in-place rewind of the PostgreSQL provider (ADR 0021): the result equals a database that was indexed straight to the round.</summary>
[Collection(PostgresServerDefinition.Name)]
public class PostgreSqlRewindTests(PostgresFixture postgres)
{
    private static readonly TokenKey Native = TokenKey.Native;

    private static RoundIndexer Indexer(PostgreSqlChainStorage storage) =>
        new(
            Scenario.Adapter(),
            new AlgoEffectNormalizer(),
            storage,
            Options.Create(new IndexerOptions { FetchConcurrency = 3, BalanceCacheEntries = 1_000 }),
            NullLogger<RoundIndexer>.Instance);

    private async Task<PostgreSqlChainStorage> IndexedAsync(ulong upTo)
    {
        var storage = await postgres.CreateStorageAsync();
        await Indexer(storage).RunAsync(upTo, CancellationToken.None);
        return storage;
    }

    /// <summary>The same database, configured to rewind (a second storage object over the same data source).</summary>
    private PostgreSqlChainStorage WithRewind(PostgreSqlChainStorage storage, string id, ulong round) =>
        new(postgres.DataSourceOf(storage), new ChainContext(ChainId.AlgorandTestnet), 4, rewind: new PartialRebuildRequest(id, round));

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public async Task A_Rewound_Database_Equals_One_Indexed_Straight_To_The_Round(ulong round)
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(round);

        var result = await WithRewind(full, "fix-1", round).RunAsync(await HashOfAsync(reference, round), null, CancellationToken.None);

        Assert.Equal(PartialRebuildOutcome.Done, result.Outcome);
        await AssertSameAsync(full, reference, round);
        Assert.Equal(IngestionState.Backfilling, (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task Indexing_Continues_After_The_Rewind_To_The_Same_Result_As_A_Straight_Run()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var straight = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(2);
        await WithRewind(full, "fix-1", 2).RunAsync(await HashOfAsync(reference, 2), null, CancellationToken.None);

        // A new indexer, as the worker starts one after the rewind: it takes its state from the database.
        await Indexer(full).RunAsync(Scenario.LastRound, CancellationToken.None);

        await AssertSameAsync(full, straight, Scenario.LastRound);
    }

    [Fact]
    public async Task Pairs_Whose_First_Effect_Is_After_The_Round_Lose_Their_Holdings_And_Their_Place_In_The_Directory()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        Assert.NotEmpty((await full.GetHoldingsAsync(Scenario.Dave, true, null, 100, CancellationToken.None)).Items); // Dave first appears in round 4

        await WithRewind(full, "fix-1", 3).RunAsync(await HashOfAsync(reference, 3), null, CancellationToken.None);

        Assert.Empty((await full.GetHoldingsAsync(Scenario.Dave, true, null, 100, CancellationToken.None)).Items);
        Assert.DoesNotContain(
            (await full.GetHoldersAtRoundAsync(Native, 3, true, null, 100, CancellationToken.None)).Items,
            h => h.Account == Scenario.Dave);
        await using var connection = await postgres.DataSourceOf(full).OpenConnectionAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM accounts_touched_by_token WHERE first_round > 3 OR last_round > 3", connection);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
        await using var byToken = new NpgsqlCommand("SELECT count(*) FROM holdings_by_token WHERE last_changed_round > 3", connection);
        Assert.Equal(0L, await byToken.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Tokens_Created_After_The_Round_Are_Gone_And_Tokens_Destroyed_After_It_Are_Alive_Again()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        await full.UpsertAsync(new TokenInfo(TokenKey.Asa(7), null, 0, "OLD", "Old", TokenStandard.Asa, true, null, 2, 5, null), CancellationToken.None);
        await full.UpsertAsync(new TokenInfo(TokenKey.Asa(8), null, 0, "NEW", "New", TokenStandard.Asa, true, null, 4, null, null), CancellationToken.None);

        await WithRewind(full, "fix-1", 3).RunAsync(await HashOfAsync(reference, 3), null, CancellationToken.None);

        var old = await full.GetAsync(TokenKey.Asa(7), CancellationToken.None);
        Assert.NotNull(old);
        Assert.Null(old.DeletedRound);
        Assert.Equal(2UL, old.CreatedRound);
        Assert.Null(await full.GetAsync(TokenKey.Asa(8), CancellationToken.None));
    }

    [Fact]
    public async Task A_Request_Id_Runs_Once_Even_After_Indexing_Passed_The_Round_Again_And_A_New_Id_Rewinds_Again()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        var hash = await HashOfAsync(reference, 3);
        var rewind = WithRewind(full, "fix-1", 3);
        Assert.False(await rewind.IsCarriedOutAsync(CancellationToken.None));
        Assert.Equal(PartialRebuildOutcome.Done, (await rewind.RunAsync(hash, null, CancellationToken.None)).Outcome);
        Assert.True(await rewind.IsCarriedOutAsync(CancellationToken.None));
        Assert.False(await WithRewind(full, "fix-2", 3).IsCarriedOutAsync(CancellationToken.None));
        await Indexer(full).RunAsync(Scenario.LastRound, CancellationToken.None);

        var again = await rewind.RunAsync(hash, null, CancellationToken.None);

        Assert.Equal(PartialRebuildOutcome.AlreadyDone, again.Outcome);
        Assert.Equal(Scenario.LastRound, (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);

        var second = await WithRewind(full, "fix-2", 3).RunAsync(hash, null, CancellationToken.None);
        Assert.Equal(PartialRebuildOutcome.Done, second.Outcome);
        Assert.Equal(3UL, (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task An_Empty_Database_And_A_Round_Beyond_The_Indexed_One_Are_Refused_And_Nothing_Changes()
    {
        var empty = await postgres.CreateStorageAsync();
        var refusedEmpty = await WithRewind(empty, "fix-1", 3).RunAsync(new byte[32], null, CancellationToken.None);
        Assert.Equal(PartialRebuildOutcome.Refused, refusedEmpty.Outcome);

        var partial = await IndexedAsync(2);
        var refused = await WithRewind(partial, "fix-1", 4).RunAsync(new byte[32], null, CancellationToken.None);
        Assert.Equal(PartialRebuildOutcome.Refused, refused.Outcome);
        Assert.Equal(2UL, (await partial.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);

        // A refusal records nothing: the same id can be used once the database has caught up.
        await Indexer(partial).RunAsync(Scenario.LastRound, CancellationToken.None);
        var reference = await IndexedAsync(4);
        Assert.Equal(PartialRebuildOutcome.Done, (await WithRewind(partial, "fix-1", 4).RunAsync(await HashOfAsync(reference, 4), null, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_Rewind_To_The_Indexed_Round_Changes_Nothing_But_Is_Recorded()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var straight = await IndexedAsync(Scenario.LastRound);

        var stateBefore = (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.State;
        var result = await WithRewind(full, "fix-1", Scenario.LastRound).RunAsync(await HashOfAsync(straight, Scenario.LastRound), null, CancellationToken.None);

        Assert.Equal((PartialRebuildOutcome.Done, 0L), (result.Outcome, result.EffectsCopied));
        Assert.Equal(stateBefore, (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.State);
        await AssertSameAsync(full, straight, Scenario.LastRound);
    }

    [Fact]
    public async Task A_Rewind_To_The_Indexed_Round_With_Another_Block_Hash_Is_Refused()
    {
        var full = await IndexedAsync(Scenario.LastRound);

        var result = await WithRewind(full, "fix-1", Scenario.LastRound).RunAsync(new byte[32], null, CancellationToken.None);

        Assert.Equal(PartialRebuildOutcome.Refused, result.Outcome);
        var status = await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);
        Assert.NotEqual(new byte[32], status!.IndexedBlockHash.ToArray());
    }

    [Fact]
    public async Task A_Block_Hash_Of_The_Wrong_Length_Is_Rejected_Before_Anything_Is_Touched()
    {
        var full = await IndexedAsync(Scenario.LastRound);

        await Assert.ThrowsAsync<ArgumentException>(() => WithRewind(full, "fix-1", 2).RunAsync(new byte[5], null, CancellationToken.None));

        Assert.Equal(Scenario.LastRound, (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task A_Failing_Rewind_Leaves_The_Database_Unchanged()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var straight = await IndexedAsync(Scenario.LastRound);
        await using (var connection = await postgres.DataSourceOf(full).OpenConnectionAsync())
        {
            // The last statement of the rewind fails against this constraint, after every delete has run.
            await using var constraint = new NpgsqlCommand("ALTER TABLE maintenance_request ADD CONSTRAINT never CHECK (request_id <> 'fix-1')", connection);
            await constraint.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => WithRewind(full, "fix-1", 2).RunAsync(new byte[32], null, CancellationToken.None));

        await AssertSameAsync(full, straight, Scenario.LastRound);
    }

    [Fact]
    public async Task Readers_See_The_Old_State_Until_The_Rewind_Commits()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(2);
        var hash = await HashOfAsync(reference, 2);

        // Holding the cursor row lock makes the rewind wait inside its transaction, after it has started, while a plain read goes through.
        await using var blocker = await postgres.DataSourceOf(full).OpenConnectionAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM chain_status FOR UPDATE", blocker, transaction))
        {
            await lockRow.ExecuteScalarAsync();
        }

        var running = WithRewind(full, "fix-1", 2).RunAsync(hash, null, CancellationToken.None);
        await Task.Delay(500);
        Assert.False(running.IsCompleted);
        Assert.Equal(Scenario.LastRound, (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);

        await transaction.RollbackAsync();
        Assert.Equal(PartialRebuildOutcome.Done, (await running).Outcome);
        Assert.Equal(2UL, (await full.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task Build_Records_And_Scans_After_The_Round_Are_Deleted()
    {
        var full = await IndexedAsync(Scenario.LastRound);
        var reference = await IndexedAsync(3);
        var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        await full.AppendAsync(new IndexingTerm(0, "v1", at), CancellationToken.None);
        await full.AppendAsync(new IndexingTerm(4, "v2", at.AddDays(1)), CancellationToken.None);

        await full.RecordAuditStopAsync(new AuditStop(4, "bad balance", 5, at, false), CancellationToken.None);

        await WithRewind(full, "fix-1", 3).RunAsync(await HashOfAsync(reference, 3), null, CancellationToken.None);

        Assert.Equal(["v1"], (await full.ListAsync(CancellationToken.None)).Select(static t => t.Version).ToArray());
        // A rebuild releases the stop of the ledger audit.
        Assert.Null(await full.GetAuditStopAsync(CancellationToken.None));
    }
}
