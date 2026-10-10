namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Applies the provider-specific schema (DDL / CQL) idempotently at bootstrap.</summary>
public interface ISchemaManager
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);
}
