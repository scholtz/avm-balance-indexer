using AvmBalanceIndexer.Domain.Storage;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

/// <summary>
/// Drops the keyspaces this deployment used before a rebuild (ADR 0020). A drop cannot be undone, so a keyspace is only dropped
/// when it is plainly an indexer keyspace of this chain that nobody writes to; anything else is reported and left alone.
/// </summary>
public sealed partial class CassandraChainStorage : IRetiredStorage
{
    public IReadOnlyList<string> Retired => _retiredKeyspaces;

    public async Task<IReadOnlyList<RetirementResult>> RemoveRetiredAsync(CancellationToken cancellationToken)
    {
        var results = new List<RetirementResult>(_retiredKeyspaces.Length);
        if (_retiredKeyspaces.Length == 0)
        {
            return results;
        }

        var session = await _source.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var keyspace in _retiredKeyspaces)
        {
            results.Add(await RemoveAsync(session, keyspace, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<RetirementResult> RemoveAsync(ISession session, string keyspace, CancellationToken cancellationToken)
    {
        var tableRows = await session.ExecuteAsync(new SimpleStatement("SELECT table_name FROM system_schema.tables WHERE keyspace_name = ?", keyspace))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var tables = tableRows.Select(static row => row.GetValue<string>("table_name")).ToHashSet(StringComparer.Ordinal);
        if (tables.Count == 0)
        {
            var found = await session.ExecuteAsync(new SimpleStatement("SELECT keyspace_name FROM system_schema.keyspaces WHERE keyspace_name = ?", keyspace))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!found.Any())
            {
                return new RetirementResult(keyspace, RetirementOutcome.Absent);
            }
        }

        if (!tables.Contains("chain_status") || !tables.Contains("writer_lease"))
        {
            return new RetirementResult(keyspace, RetirementOutcome.Refused, "it has no chain_status and writer_lease tables, so it is not a keyspace of this indexer");
        }

        // The names come from the validated configuration (identifier pattern), never from a caller.
        var chains = await session.ExecuteAsync(new SimpleStatement($"SELECT chain FROM {keyspace}.chain_status"))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var foreign = chains.Select(static row => row.GetValue<string>("chain")).FirstOrDefault(chain => !string.Equals(chain, _chain, StringComparison.Ordinal));
        if (foreign is not null)
        {
            return new RetirementResult(keyspace, RetirementOutcome.Refused, $"it holds data of {foreign}, this deployment serves {_chain}");
        }

        // A lease row lives only while its owner renews it (TTL): a row means a writer of another release still indexes into it.
        // Read by key at SERIAL, like the lease itself is written (a lightweight transaction); only this chain can be in there.
        var leases = await session.ExecuteAsync(new SimpleStatement($"SELECT owner FROM {keyspace}.writer_lease WHERE chain = ?", _chain).SetConsistencyLevel(ConsistencyLevel.Serial))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (leases.FirstOrDefault() is { } lease)
        {
            return new RetirementResult(keyspace, RetirementOutcome.InUse, $"writer {lease.GetValue<string>("owner")} still holds its lease");
        }

        await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE IF EXISTS {keyspace}")).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new RetirementResult(keyspace, RetirementOutcome.Removed);
    }
}
