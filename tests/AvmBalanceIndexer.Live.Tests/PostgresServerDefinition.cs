using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Live.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresServerDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
