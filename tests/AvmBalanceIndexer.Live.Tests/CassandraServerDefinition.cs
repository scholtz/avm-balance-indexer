using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Live.Tests;

[CollectionDefinition(Name)]
public sealed class CassandraServerDefinition : ICollectionFixture<CassandraFixture>
{
    public const string Name = "cassandra";
}
