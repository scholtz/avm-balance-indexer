using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Storage.Cassandra;

/// <summary>Applies embedded Schema/*.cql statements against the configured keyspace.</summary>
public sealed class CassandraSchemaManager(IOptions<StorageOptions> options) : ISchemaManager
{
    private readonly CassandraOptions _options = options.Value.Cassandra;

    public Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        _ = _options;
        throw new NotImplementedException("Schema bootstrap is implemented in a follow-up issue.");
    }
}
