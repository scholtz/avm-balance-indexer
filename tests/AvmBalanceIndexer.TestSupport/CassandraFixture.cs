using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Storage.Cassandra;
using Testcontainers.Cassandra;
using Xunit;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// Provides a Cassandra node for tests. Uses the node named by <c>AVM_TEST_CASSANDRA</c> (<c>host:port</c>, with the
/// datacenter in <c>AVM_TEST_CASSANDRA_DC</c>, default <c>dc1</c>) when set; otherwise starts a disposable
/// container. Every <see cref="CreateStorageAsync"/> call gets a brand-new keyspace, so tests are isolated.
/// </summary>
public sealed class CassandraFixture : IAsyncLifetime
{
    public const string ContactVariable = "AVM_TEST_CASSANDRA";
    public const string DatacenterVariable = "AVM_TEST_CASSANDRA_DC";

    private CassandraContainer? _container;
    private string _host = string.Empty;
    private int _port;
    private string _datacenter = "dc1";
    private int _keyspaceCounter;
    private readonly System.Collections.Concurrent.ConcurrentBag<CassandraSessionSource> _sources = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CassandraChainStorage, (CassandraSessionSource Source, CassandraOptions Options)> _storages = new();

    // The node starts when the first test asks for a keyspace, not when the collection is created: a collection whose tests are all
    // skipped (the live tests under AVM_SKIP_LIVE_TESTS=1) must not pay for a container, or need Docker.
    private readonly object _startGate = new();
    private Task? _starting;

    public Task InitializeAsync() => Task.CompletedTask;

    private Task EnsureStartedAsync()
    {
        lock (_startGate)
        {
            return _starting ??= StartAsync();
        }
    }

    private async Task StartAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ContactVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var parts = configured.Split(':');
            _host = parts[0];
            _port = parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 9042;
            _datacenter = Environment.GetEnvironmentVariable(DatacenterVariable) ?? _datacenter;
            return;
        }

        _container = new CassandraBuilder("cassandra:5.0").Build();
        await _container.StartAsync().ConfigureAwait(false);
        _host = _container.Hostname;
        _port = _container.GetMappedPublicPort(9042);
    }

    public async Task DisposeAsync()
    {
        if (_container is null)
        {
            // A shared node outlives the run; leave no keyspaces behind.
            foreach (var (_, (source, options)) in _storages)
            {
                try
                {
                    var session = await source.GetSessionAsync(CancellationToken.None).ConfigureAwait(false);
                    await session.ExecuteAsync(new global::Cassandra.SimpleStatement($"DROP KEYSPACE IF EXISTS {options.Keyspace}")).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best effort cleanup.
                }
            }
        }

        foreach (var source in _sources)
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private CassandraOptions NewOptions(bool createKeyspace = true) => new()
    {
        ContactPoints = [_host],
        Port = _port,
        LocalDatacenter = _datacenter,
        Keyspace = $"avm_test_{Environment.ProcessId}_{Interlocked.Increment(ref _keyspaceCounter)}_{Guid.NewGuid():N}"[..48],
        CreateKeyspace = createKeyspace,
        ReplicationFactor = 1,
    };

    /// <summary>Creates a fresh keyspace, applies the schema (unless told not to) and returns the storage bound to it.</summary>
    public async Task<CassandraChainStorage> CreateStorageAsync(string chain = ChainId.AlgorandTestnet, bool applySchema = true, TimeSpan? readDeadline = null, TimeSpan? leaseTtl = null)
    {
        await EnsureStartedAsync().ConfigureAwait(false);
        var options = NewOptions();
        var source = new CassandraSessionSource(options);
        _sources.Add(source);
        var storage = new CassandraChainStorage(source, new ChainContext(chain), options, readDeadline, leaseTtl);
        _storages[storage] = (source, options);
        if (applySchema)
        {
            await storage.EnsureSchemaAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return storage;
    }

    /// <summary>Keyspace a storage created by this fixture lives in.</summary>
    public string KeyspaceOf(CassandraChainStorage storage) => _storages[storage].Options.Keyspace;

    /// <summary>The driver session of a storage created by this fixture, to plant or inspect rows the storage API cannot.</summary>
    public Task<global::Cassandra.ISession> SessionOfAsync(CassandraChainStorage storage) =>
        _storages[storage].Source.GetSessionAsync(CancellationToken.None);
}
