using System.Globalization;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var session = await _source.GetSessionAsync(cancellationToken).ConfigureAwait(false);

        if (_createKeyspace)
        {
            // Name and datacenter are validated identifiers; the replication factor is a number.
            var keyspace = string.Create(
                CultureInfo.InvariantCulture,
                $"CREATE KEYSPACE IF NOT EXISTS {_keyspace} WITH replication = {{'class': 'NetworkTopologyStrategy', '{_localDatacenter}': {_replicationFactor}}}");
            await session.ExecuteAsync(new SimpleStatement(keyspace)).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // The driver waits for schema agreement after every DDL statement.
        session.ChangeKeyspace(_keyspace);
        foreach (var statement in ReadEmbeddedStatements())
        {
            await session.ExecuteAsync(new SimpleStatement(statement)).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> ReadEmbeddedStatements()
    {
        var assembly = typeof(CassandraChainStorage).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(static n => n.EndsWith(".cql", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded schema script {name} could not be opened.");
            using var reader = new StreamReader(stream);
            var script = string.Join(
                '\n',
                reader.ReadToEnd().Split('\n').Where(static line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
            foreach (var statement in script.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                yield return statement;
            }
        }
    }
}
