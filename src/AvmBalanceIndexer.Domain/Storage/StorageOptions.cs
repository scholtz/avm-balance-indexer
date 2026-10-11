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

    public int Port { get; set; } = 9042;

    public string Keyspace { get; set; } = "avm_indexer";

    /// <summary>Let the schema manager create the keyspace (NetworkTopologyStrategy with <see cref="ReplicationFactor"/> replicas in <see cref="LocalDatacenter"/>).</summary>
    public bool CreateKeyspace { get; set; } = true;

    /// <summary>Replicas in the local datacenter for a keyspace created by the schema manager. Reads and writes use LOCAL_QUORUM.</summary>
    public int ReplicationFactor { get; set; } = 3;

    public string LocalDatacenter { get; set; } = "dc1";

    /// <summary>Encrypt the connection (TLS with the system trust store). Credentials are sent in clear text without it.</summary>
    public bool UseTls { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }
}
