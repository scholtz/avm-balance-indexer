using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>Runs the shared contract suite against a real PostgreSQL server (Testcontainers or AVM_TEST_POSTGRES).</summary>
[Collection(PostgresServerDefinition.Name)]
public class PostgreSqlContractTests(PostgresFixture postgres) : StorageContractTests
{
    protected override async Task<IChainStorage> CreateStorageAsync() => await postgres.CreateStorageAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresServerDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
