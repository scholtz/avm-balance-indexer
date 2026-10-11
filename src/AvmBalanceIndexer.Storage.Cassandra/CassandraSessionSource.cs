using AvmBalanceIndexer.Domain.Storage;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

/// <summary>
/// Owns the driver cluster and connects lazily on first use, so an API pod can start before Cassandra is reachable.
/// A failed connection attempt is not remembered. The session is created without a keyspace; every statement
/// qualifies its tables.
/// </summary>
public sealed class CassandraSessionSource : IAsyncDisposable
{
    private readonly CassandraOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Cluster? _cluster;
    private ISession? _session;

    public CassandraSessionSource(CassandraOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ISession> GetSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is { } connected)
        {
            return connected;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is { } raced)
            {
                return raced;
            }

            _cluster ??= Build(_options);
            _session = await _cluster.ConnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return _session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.ShutdownAsync().ConfigureAwait(false);
        }

        if (_cluster is not null)
        {
            await _cluster.ShutdownAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private static Cluster Build(CassandraOptions options)
    {
        var builder = global::Cassandra.Cluster.Builder()
            .AddContactPoints(options.ContactPoints)
            .WithPort(options.Port)
            .WithQueryOptions(new QueryOptions().SetConsistencyLevel(ConsistencyLevel.LocalQuorum).SetPageSize(1_100))
            .WithLoadBalancingPolicy(new TokenAwarePolicy(new DCAwareRoundRobinPolicy(options.LocalDatacenter)))
            .WithSocketOptions(new SocketOptions().SetReadTimeoutMillis(30_000));
        if (!string.IsNullOrEmpty(options.Username))
        {
            builder = builder.WithCredentials(options.Username, options.Password ?? string.Empty);
        }

        return builder.Build();
    }
}
