using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Storage.PostgreSql;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// Provides a PostgreSQL server for tests. Uses the server named by <c>AVM_TEST_POSTGRES</c> when set (CI services,
/// local instances); otherwise starts a disposable container. Every <see cref="CreateStorageAsync"/> call gets a
/// brand-new database, so tests are isolated.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ConnectionVariable = "AVM_TEST_POSTGRES";

    private PostgreSqlContainer? _container;
    private string _adminConnectionString = string.Empty;
    private int _databaseCounter;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _adminConnectionString = configured;
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _container.StartAsync().ConfigureAwait(false);
        _adminConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Creates an empty database, applies the schema (unless told not to) and returns the storage bound to it.</summary>
    public async Task<PostgreSqlChainStorage> CreateStorageAsync(string chain = ChainId.AlgorandTestnet, int accountPartitions = 4, bool applySchema = true)
    {
        var name = $"avm_test_{Environment.ProcessId}_{Interlocked.Increment(ref _databaseCounter)}_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name, Pooling = true, MaxPoolSize = 8 }.ConnectionString;
        var storage = new PostgreSqlChainStorage(NpgsqlDataSource.Create(connectionString), new ChainContext(chain), accountPartitions);
        if (applySchema)
        {
            await storage.EnsureSchemaAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return storage;
    }
}
