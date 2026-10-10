using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Storage.PostgreSql;

/// <summary>Applies embedded Schema/*.sql scripts and creates the fixed partition set.</summary>
public sealed class PostgreSqlSchemaManager(IOptions<StorageOptions> options) : ISchemaManager
{
    private readonly PostgreSqlOptions _options = options.Value.PostgreSql;

    public Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        _ = _options;
        throw new NotImplementedException("Schema bootstrap is implemented in a follow-up issue.");
    }
}
