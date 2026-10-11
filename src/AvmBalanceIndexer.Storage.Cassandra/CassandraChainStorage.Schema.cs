using System.Globalization;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var session = await _source.GetSessionAsync(cancellationToken).ConfigureAwait(false);

        // Concurrent DDL from several replicas can leave Cassandra with disagreeing schema versions, so in the steady state (several
        // replicas start together, the schema exists) nothing is sent at all.
        if (await SchemaExistsAsync(session, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

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
            await session.ExecuteAsync(new SimpleStatement(Qualify(statement))).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> SchemaExistsAsync(ISession session, CancellationToken cancellationToken)
    {
        var rows = await session.ExecuteAsync(new SimpleStatement("SELECT table_name FROM system_schema.tables WHERE keyspace_name = ?", _keyspace))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var existing = rows.Select(static row => row.GetValue<string>("table_name")).ToHashSet(StringComparer.Ordinal);
        var statements = ReadEmbeddedStatements().ToArray();
        var expectedTables = statements
            .Select(static statement => System.Text.RegularExpressions.Regex.Match(statement, @"^CREATE TABLE IF NOT EXISTS (\w+)"))
            .Where(static match => match.Success)
            .Select(static match => match.Groups[1].Value);
        if (!expectedTables.All(existing.Contains))
        {
            return false;
        }

        var columnRows = await session.ExecuteAsync(new SimpleStatement("SELECT table_name, column_name FROM system_schema.columns WHERE keyspace_name = ?", _keyspace))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var columns = columnRows.Select(static row => (row.GetValue<string>("table_name"), row.GetValue<string>("column_name"))).ToHashSet();
        return statements
            .Select(static statement => System.Text.RegularExpressions.Regex.Match(statement, @"^ALTER TABLE (\w+) ADD (?:IF NOT EXISTS )?(\w+) "))
            .Where(static match => match.Success)
            .All(match => columns.Contains((match.Groups[1].Value, match.Groups[2].Value)));
    }

    /// <summary>Puts the keyspace in front of every created table so the DDL does not depend on the session's current keyspace.</summary>
    private string QualifyTables(string statement) =>
        System.Text.RegularExpressions.Regex.Replace(
            statement,
            @"^CREATE TABLE IF NOT EXISTS (\w+)",
            $"CREATE TABLE IF NOT EXISTS {_keyspace}.$1");

    private string Qualify(string statement) =>
        System.Text.RegularExpressions.Regex.Replace(QualifyTables(statement), @"^ALTER TABLE (\w+)", $"ALTER TABLE {_keyspace}.$1");

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
