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
        foreach (var statement in ReadEmbeddedStatements())
        {
            await session.ExecuteAsync(new SimpleStatement(QualifyTables(statement))).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Puts the keyspace in front of every created table so the DDL does not depend on the session's current keyspace.</summary>
    private string QualifyTables(string statement) =>
        System.Text.RegularExpressions.Regex.Replace(
            statement,
            @"^CREATE TABLE IF NOT EXISTS (\w+)",
            $"CREATE TABLE IF NOT EXISTS {_keyspace}.$1");

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
