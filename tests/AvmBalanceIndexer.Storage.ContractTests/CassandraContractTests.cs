using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>Runs the shared contract suite against a real Cassandra node (Testcontainers or AVM_TEST_CASSANDRA).</summary>
[Collection(CassandraServerDefinition.Name)]
public class CassandraContractTests(CassandraFixture cassandra) : StorageContractTests
{
    protected override async Task<IChainStorage> CreateStorageAsync() => await cassandra.CreateStorageAsync();
}

[CollectionDefinition(Name)]
public sealed class CassandraServerDefinition : ICollectionFixture<CassandraFixture>
{
    public const string Name = "cassandra";
}
