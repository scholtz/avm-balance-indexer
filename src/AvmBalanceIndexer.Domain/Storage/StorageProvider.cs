namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Storage backend selected per chain deployment via the Storage:Provider setting.</summary>
public enum StorageProvider
{
    PostgreSql = 0,
    Cassandra = 1,
}
