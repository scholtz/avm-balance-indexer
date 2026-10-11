using System.Collections.Concurrent;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

/// <summary>
/// Cassandra implementation of the storage contract for one chain (one keyspace). There is no transaction across
/// partitions, so a round is committed as idempotent upserts of every query table followed by a lightweight
/// transaction that advances the publication cursor. Readers only trust data at or below the published round;
/// a crash before publication leaves rows that the replay of the same round overwrites with identical values.
/// </summary>
public sealed partial class CassandraChainStorage : IChainStorage
{
    /// <summary>
    /// Number of shards of the token-oriented tables (accounts are assigned by hash). Part of the on-disk layout:
    /// changing it requires a replay into a fresh keyspace.
    /// </summary>
    internal const int TokenShards = 16;

    private const int MaxPageSize = 1000;

    /// <summary>Requests in flight per operation; keeps a genesis round with many thousand accounts from flooding the driver queue.</summary>
    private const int MaxInFlight = 64;

    private readonly CassandraSessionSource _source;
    private readonly string _chain;
    private readonly string _keyspace;
    private readonly string _localDatacenter;
    private readonly int _replicationFactor;
    private readonly bool _createKeyspace;
    private readonly ConcurrentDictionary<string, Task<PreparedStatement>> _prepared = new();

    public CassandraChainStorage(CassandraSessionSource source, ChainContext chain, CassandraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _chain = chain?.Chain ?? throw new ArgumentNullException(nameof(chain));
        _keyspace = CassandraOptionsValidator.IsValidIdentifier(options.Keyspace)
            ? options.Keyspace
            : throw new ArgumentException("Invalid Cassandra keyspace name.", nameof(options));
        _localDatacenter = CassandraOptionsValidator.IsValidDatacenter(options.LocalDatacenter)
            ? options.LocalDatacenter
            : throw new ArgumentException("Invalid Cassandra datacenter name.", nameof(options));
        _replicationFactor = options.ReplicationFactor is >= 1 and <= 9
            ? options.ReplicationFactor
            : throw new ArgumentOutOfRangeException(nameof(options), "ReplicationFactor must be between 1 and 9.");
        _createKeyspace = options.CreateKeyspace;
    }

    /// <summary>Statement text for a table of this chain's keyspace (the keyspace name is validated, never caller supplied).</summary>
    private string Table(string name) => $"{_keyspace}.{name}";

    private async Task<BoundStatement> BindAsync(string cql, CancellationToken cancellationToken, params object?[] values)
    {
        var session = await _source.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        Task<PreparedStatement> preparing = _prepared.GetOrAdd(cql, text => session.PrepareAsync(text));
        try
        {
            var prepared = await preparing.WaitAsync(cancellationToken).ConfigureAwait(false);
            return prepared.Bind(values);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed preparation (for example a keyspace that does not exist yet) must not be remembered.
            _prepared.TryRemove(new KeyValuePair<string, Task<PreparedStatement>>(cql, preparing));
            throw;
        }
    }

    private async Task<RowSet> ExecuteAsync(string cql, CancellationToken cancellationToken, params object?[] values)
    {
        var statement = await BindAsync(cql, cancellationToken, values).ConfigureAwait(false);
        return await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RowSet> ExecuteAsync(IStatement statement, CancellationToken cancellationToken)
    {
        var session = await _source.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        return await session.ExecuteAsync(statement).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="work"/> for every item with a bounded number of requests in flight; results keep the input order.
    /// After the first failure no further item is started, and the method only returns once nothing it started is still running.
    /// A caller that runs several of these at once passes one shared <paramref name="gate"/> to bound them together.
    /// </summary>
    private static async Task<TResult[]> RunBoundedAsync<TItem, TResult>(
        IReadOnlyCollection<TItem> items,
        Func<TItem, Task<TResult>> work,
        CancellationToken cancellationToken,
        SemaphoreSlim? gate = null)
    {
        var results = new TResult[items.Count];
        if (items.Count == 0)
        {
            return results;
        }

        var ownGate = gate is null ? new SemaphoreSlim(MaxInFlight) : null;
        var limit = gate ?? ownGate!;
        Exception? failure = null;
        var index = 0;
        var tasks = new List<Task>(items.Count);
        try
        {
            foreach (var item in items)
            {
                var slot = index++;
                await limit.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (Volatile.Read(ref failure) is not null)
                {
                    limit.Release();
                    break;
                }

                tasks.Add(RunOneAsync(item, slot));
            }
        }
        finally
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
            ownGate?.Dispose();
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return results;

        async Task RunOneAsync(TItem item, int slot)
        {
            try
            {
                results[slot] = await work(item).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
            }
            finally
            {
                limit.Release();
            }
        }
    }

    private static async Task RunBoundedAsync<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, Task> work, CancellationToken cancellationToken) =>
        await RunBoundedAsync<TItem, bool>(items, async item => { await work(item).ConfigureAwait(false); return true; }, cancellationToken).ConfigureAwait(false);

    private void EnsureChain(string chain)
    {
        if (!string.Equals(chain, _chain, StringComparison.Ordinal))
        {
            throw new ArgumentException($"This storage serves chain {_chain} but was asked for {chain}.", nameof(chain));
        }
    }

    /// <summary>Token ids are stored as signed bigint; ids beyond its range would sort wrongly, so they are refused.</summary>
    private static long StorageId(ulong tokenId) =>
        tokenId <= long.MaxValue
            ? (long)tokenId
            : throw new ArgumentOutOfRangeException(nameof(tokenId), tokenId, "Token ids above 2^63 - 1 are not supported by the Cassandra provider.");

    /// <summary>Stable shard of an account in the token-oriented tables (FNV-1a over the public key).</summary>
    internal static short ShardOf(Address account)
    {
        var hash = 2166136261u;
        foreach (var b in account.Bytes)
        {
            hash = (hash ^ b) * 16777619u;
        }

        return (short)(hash % TokenShards);
    }
}
