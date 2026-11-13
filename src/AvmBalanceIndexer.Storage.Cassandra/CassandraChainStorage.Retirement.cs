using System.Globalization;
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

    public async Task<RetirementResult> RemoveRetiredAsync(string name, CancellationToken cancellationToken)
    {
        // Only names of the validated configuration ever reach a statement.
        var keyspace = _retiredKeyspaces.FirstOrDefault(retired => string.Equals(retired, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{name} is not listed in Storage:Cassandra:RetiredKeyspaces.", nameof(name));
        var session = await _source.GetSessionAsync(cancellationToken).ConfigureAwait(false);

        var tables = await TablesOfAsync(session, keyspace, cancellationToken).ConfigureAwait(false);
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

        var statuses = (await session.ExecuteAsync(new SimpleStatement($"SELECT chain, updated_at FROM {keyspace}.chain_status").SetConsistencyLevel(ConsistencyLevel.LocalQuorum))
            .WaitAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var foreign = statuses.Select(static row => row.GetValue<string>("chain")).FirstOrDefault(chain => !string.Equals(chain, _chain, StringComparison.Ordinal));
        if (foreign is not null)
        {
            return new RetirementResult(keyspace, RetirementOutcome.Refused, $"it holds data of {foreign}, this deployment serves {_chain}");
        }

        // A lease row lives only while its owner renews it (TTL). Every row counts, whatever its chain: a keyspace that has not
        // published a round yet says nothing about whose it is.
        var leases = await session.ExecuteAsync(new SimpleStatement($"SELECT chain, owner FROM {keyspace}.writer_lease").SetConsistencyLevel(ConsistencyLevel.LocalQuorum))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (leases.FirstOrDefault() is { } lease)
        {
            return new RetirementResult(keyspace, RetirementOutcome.InUse, $"a writer of {lease.GetValue<string>("chain")} holds its lease");
        }

        // The lease row is missing for a moment whenever a writer restarts, so one look is not proof. A writer also keeps the
        // status timestamp moving (even when idle at the tip): only a keyspace that has been silent for the quiet period is
        // abandoned. Without a published round there is no such evidence, and nothing says whose keyspace it is.
        var lastWrite = statuses.Select(static row => row.GetValue<DateTimeOffset?>("updated_at")).Max();
        if (lastWrite is not { } written)
        {
            return new RetirementResult(keyspace, RetirementOutcome.Refused, "it has no published round, so nothing shows that it belongs to this chain and is abandoned; it holds no data worth the risk, drop it by hand");
        }

        if (DateTimeOffset.UtcNow - written < _retirementQuietPeriod)
        {
            return new RetirementResult(
                keyspace,
                RetirementOutcome.InUse,
                string.Create(CultureInfo.InvariantCulture, $"its status was last updated at {written.UtcDateTime:yyyy-MM-dd HH:mm} UTC; it must be untouched for {_retirementQuietPeriod.TotalMinutes:0} min"));
        }

        // Last look, at SERIAL like the lease is taken (a lightweight transaction): a writer that is acquiring it right now is seen.
        var taken = await session.ExecuteAsync(new SimpleStatement($"SELECT owner FROM {keyspace}.writer_lease WHERE chain = ?", _chain).SetConsistencyLevel(ConsistencyLevel.Serial))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (taken.Any())
        {
            return new RetirementResult(keyspace, RetirementOutcome.InUse, $"a writer of {_chain} holds its lease");
        }

        await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE IF EXISTS {keyspace}")).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new RetirementResult(keyspace, RetirementOutcome.Removed);
    }

    private static async Task<HashSet<string>> TablesOfAsync(ISession session, string keyspace, CancellationToken cancellationToken)
    {
        var rows = await session.ExecuteAsync(new SimpleStatement("SELECT table_name FROM system_schema.tables WHERE keyspace_name = ?", keyspace))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(static row => row.GetValue<string>("table_name")).ToHashSet(StringComparer.Ordinal);
    }
}
