using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Storage.Cassandra;
using AvmBalanceIndexer.TestSupport;
using Cassandra;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>Retired keyspaces (ADR 0020): what the writer may drop after a rebuild, and what it must leave alone.</summary>
[Collection(CassandraServerDefinition.Name)]
public class CassandraRetirementTests(CassandraFixture cassandra)
{
    private static ChainStatus Status(string chain, ulong round, DateTimeOffset? updatedAt = null) =>
        new(chain, round, TestChain.FakeHash(round), null, IngestionState.Following, updatedAt ?? DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task A_Retired_Keyspace_Of_The_Same_Chain_Is_Dropped_And_The_Request_Can_Be_Repeated()
    {
        var old = await cassandra.CreateStorageAsync();
        await old.WriteRoundAsync(Status(ChainId.AlgorandTestnet, 0), [], CancellationToken.None);
        var oldKeyspace = cassandra.KeyspaceOf(old);
        var rebuilt = await cassandra.CreateStorageAsync(retiredKeyspaces: [oldKeyspace.ToUpperInvariant()]);
        await rebuilt.WriteRoundAsync(Status(ChainId.AlgorandTestnet, 0), [], CancellationToken.None);

        // The configured spelling and the stored (lower-case) one both name the request.
        var first = await rebuilt.RemoveRetiredAsync(oldKeyspace.ToUpperInvariant(), CancellationToken.None);
        var second = await rebuilt.RemoveRetiredAsync(Assert.Single(rebuilt.Retired), CancellationToken.None);

        Assert.Equal(new RetirementResult(oldKeyspace, RetirementOutcome.Removed), first);
        Assert.Equal(new RetirementResult(oldKeyspace, RetirementOutcome.Absent), second);
        Assert.False(await ExistsAsync(rebuilt, oldKeyspace));

        // The data this deployment uses is untouched.
        Assert.Equal(0UL, (await rebuilt.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task A_Keyspace_Without_A_Published_Round_Is_Kept_Because_Nothing_Shows_Whose_It_Is()
    {
        // It may be the keyspace another release created a moment ago, between applying the schema and taking the lease.
        var old = await cassandra.CreateStorageAsync();
        var oldKeyspace = cassandra.KeyspaceOf(old);
        var rebuilt = await cassandra.CreateStorageAsync(retiredKeyspaces: [oldKeyspace]);

        var result = await rebuilt.RemoveRetiredAsync(oldKeyspace, CancellationToken.None);

        Assert.Equal(RetirementOutcome.Refused, result.Outcome);
        Assert.Contains("no published round", result.Reason, StringComparison.Ordinal);
        Assert.True(await ExistsAsync(rebuilt, oldKeyspace));
    }

    [Fact]
    public async Task A_Keyspace_That_Holds_Another_Chain_Is_Kept()
    {
        var voi = await cassandra.CreateStorageAsync(ChainId.Voi);
        await voi.WriteRoundAsync(Status(ChainId.Voi, 0), [], CancellationToken.None);
        var voiKeyspace = cassandra.KeyspaceOf(voi);
        var rebuilt = await cassandra.CreateStorageAsync(retiredKeyspaces: [voiKeyspace]);

        var result = await rebuilt.RemoveRetiredAsync(voiKeyspace, CancellationToken.None);

        Assert.Equal(RetirementOutcome.Refused, result.Outcome);
        Assert.Contains(ChainId.Voi, result.Reason, StringComparison.Ordinal);
        Assert.Equal(0UL, (await voi.GetAsync(ChainId.Voi, CancellationToken.None))!.IndexedRound);
    }

    [Fact]
    public async Task A_Keyspace_Another_Chain_Has_Just_Started_In_Is_Kept_While_Its_Writer_Holds_The_Lease()
    {
        // Schema applied and the lease taken, but no round published yet: the status table cannot say whose keyspace it is.
        var voi = await cassandra.CreateStorageAsync(ChainId.Voi);
        var voiKeyspace = cassandra.KeyspaceOf(voi);
        var rebuilt = await cassandra.CreateStorageAsync(retiredKeyspaces: [voiKeyspace]);
        var lease = await ((IWriterLeaseProvider)voi).TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(lease);
        await using (lease)
        {
            var result = await rebuilt.RemoveRetiredAsync(voiKeyspace, CancellationToken.None);

            Assert.Equal(RetirementOutcome.InUse, result.Outcome);
            Assert.Contains(ChainId.Voi, result.Reason, StringComparison.Ordinal);
            Assert.True(await ExistsAsync(rebuilt, voiKeyspace));
        }
    }

    [Fact]
    public async Task A_Keyspace_That_Is_Not_An_Indexer_Keyspace_Is_Kept()
    {
        var rebuilt = await cassandra.CreateStorageAsync();
        var session = await cassandra.SessionOfAsync(rebuilt);
        var foreign = $"other_app_{Guid.NewGuid():N}";
        var empty = $"empty_{Guid.NewGuid():N}";
        await session.ExecuteAsync(new SimpleStatement($"CREATE KEYSPACE {foreign} WITH replication = {{'class': 'SimpleStrategy', 'replication_factor': 1}}"));
        await session.ExecuteAsync(new SimpleStatement($"CREATE TABLE {foreign}.customers (id int PRIMARY KEY)"));
        await session.ExecuteAsync(new SimpleStatement($"CREATE KEYSPACE {empty} WITH replication = {{'class': 'SimpleStrategy', 'replication_factor': 1}}"));
        try
        {
            var storage = await cassandra.CreateStorageAsync(retiredKeyspaces: [foreign, empty]);

            Assert.Equal(RetirementOutcome.Refused, (await storage.RemoveRetiredAsync(foreign, CancellationToken.None)).Outcome);
            Assert.Equal(RetirementOutcome.Refused, (await storage.RemoveRetiredAsync(empty, CancellationToken.None)).Outcome);
            Assert.True(await ExistsAsync(rebuilt, foreign));
            Assert.True(await ExistsAsync(rebuilt, empty));
        }
        finally
        {
            await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE IF EXISTS {foreign}"));
            await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE IF EXISTS {empty}"));
        }
    }

    [Fact]
    public async Task A_Keyspace_Another_Writer_Still_Indexes_Into_Is_Kept_Until_Its_Lease_Is_Gone()
    {
        var old = await cassandra.CreateStorageAsync();
        await old.WriteRoundAsync(Status(ChainId.AlgorandTestnet, 0), [], CancellationToken.None);
        var oldKeyspace = cassandra.KeyspaceOf(old);
        var rebuilt = await cassandra.CreateStorageAsync(retiredKeyspaces: [oldKeyspace]);
        var lease = await ((IWriterLeaseProvider)old).TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(lease);

        var whileHeld = await rebuilt.RemoveRetiredAsync(oldKeyspace, CancellationToken.None);
        await lease.DisposeAsync();
        var afterRelease = await rebuilt.RemoveRetiredAsync(oldKeyspace, CancellationToken.None);

        Assert.Equal(RetirementOutcome.InUse, whileHeld.Outcome);
        Assert.Equal(RetirementOutcome.Removed, afterRelease.Outcome);
    }

    [Fact]
    public async Task A_Keyspace_Written_To_Recently_Is_Kept_Even_Without_A_Lease_Row()
    {
        // A writer that is restarting has no lease row for a moment; its recent status update still shows the keyspace is alive.
        var old = await cassandra.CreateStorageAsync();
        await old.WriteRoundAsync(Status(ChainId.AlgorandTestnet, 0, DateTimeOffset.UtcNow.AddMinutes(-5)), [], CancellationToken.None);
        var oldKeyspace = cassandra.KeyspaceOf(old);
        var waiting = await cassandra.CreateStorageAsync(retiredKeyspaces: [oldKeyspace], retiredQuietMinutes: 60);
        var patient = await cassandra.CreateStorageAsync(retiredKeyspaces: [oldKeyspace], retiredQuietMinutes: 4);

        var tooEarly = await waiting.RemoveRetiredAsync(oldKeyspace, CancellationToken.None);
        Assert.Equal(RetirementOutcome.InUse, tooEarly.Outcome);
        Assert.Contains("60 min", tooEarly.Reason, StringComparison.Ordinal);
        Assert.True(await ExistsAsync(waiting, oldKeyspace));

        // Silent for longer than the quiet period: abandoned.
        Assert.Equal(RetirementOutcome.Removed, (await patient.RemoveRetiredAsync(oldKeyspace, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_Keyspace_That_Never_Existed_Is_Reported_Absent()
    {
        var storage = await cassandra.CreateStorageAsync(retiredKeyspaces: ["never_created_keyspace"]);

        Assert.Equal(new RetirementResult("never_created_keyspace", RetirementOutcome.Absent), await storage.RemoveRetiredAsync("never_created_keyspace", CancellationToken.None));
    }

    [Fact]
    public async Task Only_Listed_Keyspaces_Can_Be_Asked_For()
    {
        var other = await cassandra.CreateStorageAsync();
        var storage = await cassandra.CreateStorageAsync();

        Assert.Empty(storage.Retired);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.RemoveRetiredAsync(cassandra.KeyspaceOf(other), CancellationToken.None));
        Assert.True(await ExistsAsync(storage, cassandra.KeyspaceOf(other)));
    }

    [Theory]
    [InlineData("avm_indexer", "keyspace this deployment uses")]
    [InlineData("AVM_Indexer", "keyspace this deployment uses")]
    [InlineData("system_schema", "system keyspace")]
    [InlineData("System", "system keyspace")]
    [InlineData("old; DROP KEYSPACE x", "not a keyspace name")]
    [InlineData("", "not a keyspace name")]
    [InlineData("avm_old\n", "not a keyspace name")]
    public void A_Retired_Name_That_Must_Never_Be_Dropped_Fails_The_Configuration(string retired, string expected)
    {
        var options = new StorageOptions
        {
            Provider = StorageProvider.Cassandra,
            Cassandra = new CassandraOptions { ContactPoints = ["localhost"], Keyspace = "avm_indexer", RetiredKeyspaces = [retired] },
        };

        var result = new CassandraOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(expected, Assert.Single(result.Failures), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new CassandraChainStorage(new CassandraSessionSource(options.Cassandra), new ChainContext(ChainId.AlgorandTestnet), options.Cassandra));
    }

    [Fact]
    public void Retired_Keyspaces_Other_Than_The_Active_One_Are_Accepted_And_A_Missing_List_Means_None()
    {
        var options = new StorageOptions
        {
            Provider = StorageProvider.Cassandra,
            Cassandra = new CassandraOptions { ContactPoints = ["localhost"], Keyspace = "avm_testnet_2", RetiredKeyspaces = ["avm_testnet", "avm_testnet_1"] },
        };
        Assert.True(new CassandraOptionsValidator().Validate(null, options).Succeeded);

        options.Cassandra.RetiredKeyspaces = null!;
        Assert.True(new CassandraOptionsValidator().Validate(null, options).Succeeded);
        Assert.Empty(new CassandraChainStorage(new CassandraSessionSource(options.Cassandra), new ChainContext(ChainId.AlgorandTestnet), options.Cassandra).Retired);

        options.Cassandra.RetiredKeyspaceQuietMinutes = -1;
        Assert.True(new CassandraOptionsValidator().Validate(null, options).Failed);
    }

    private async Task<bool> ExistsAsync(CassandraChainStorage storage, string keyspace)
    {
        var session = await cassandra.SessionOfAsync(storage);
        var rows = await session.ExecuteAsync(new SimpleStatement("SELECT keyspace_name FROM system_schema.keyspaces WHERE keyspace_name = ?", keyspace));
        return rows.Any();
    }
}
