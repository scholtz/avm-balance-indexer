namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Bound from the Storage configuration section. Only the selected provider is opened.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.PostgreSql;

    public PostgreSqlOptions PostgreSql { get; set; } = new();

    public CassandraOptions Cassandra { get; set; } = new();
}

public sealed class PostgreSqlOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Fixed hash partition count for effects_by_account_token; chosen once per deployment.</summary>
    public int AccountHashPartitions { get; set; } = 64;
}

public sealed class CassandraOptions
{
    public string[] ContactPoints { get; set; } = [];

    public string Keyspace { get; set; } = "avm_indexer";

    public string LocalDatacenter { get; set; } = "dc1";

    public string? Username { get; set; }

    public string? Password { get; set; }
}
