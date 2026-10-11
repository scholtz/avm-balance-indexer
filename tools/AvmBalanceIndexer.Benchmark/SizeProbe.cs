using System.Diagnostics;
using Cassandra;
using Npgsql;

namespace AvmBalanceIndexer.Benchmark;

/// <summary>Measures the bytes the indexed data occupies on disk in each provider.</summary>
public static class SizeProbe
{
    public static async Task<(StorageSizeResult Size, string Version)> PostgreSqlAsync(string connectionString, long effects, int rounds, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Statistics and visibility map are refreshed so the numbers do not depend on when autovacuum last ran.
        await using (var analyze = new NpgsqlCommand("VACUUM (ANALYZE)", connection))
        {
            await analyze.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Partitions are folded into their parent table; every table counts with its indexes and TOAST data.
        await using var tables = new NpgsqlCommand(
            """
            SELECT coalesce(parent.relname, c.relname) AS name, sum(pg_total_relation_size(c.oid))::bigint AS bytes
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = current_schema()
            LEFT JOIN pg_inherits i ON i.inhrelid = c.oid
            LEFT JOIN pg_class parent ON parent.oid = i.inhparent
            WHERE c.relkind IN ('r', 'm') AND c.relispartition IS NOT NULL
            GROUP BY 1
            ORDER BY 2 DESC
            """,
            connection);
        var list = new List<TableSize>();
        await using (var reader = await tables.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new TableSize(reader.GetString(0), reader.GetInt64(1)));
            }
        }

        await using var version = new NpgsqlCommand("SHOW server_version", connection);
        var serverVersion = (string?)await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? "unknown";
        var total = list.Sum(static t => t.Bytes);
        return (Describe(total, effects, rounds, includesUnflushed: false, list), "PostgreSQL " + serverVersion);
    }

    public static async Task<(StorageSizeResult Size, string Version)> CassandraAsync(string host, int port, string datacenter, string keyspace, string? container, long effects, int rounds, CancellationToken cancellationToken)
    {
        var flushed = false;
        if (!string.IsNullOrWhiteSpace(container))
        {
            flushed = RunProcess("docker", $"exec {container} nodetool flush {keyspace}");
        }

        var cluster = Cluster.Builder().AddContactPoint(host).WithPort(port).WithLoadBalancingPolicy(new DCAwareRoundRobinPolicy(datacenter)).Build();
        using var _ = cluster;
        var session = await cluster.ConnectAsync().ConfigureAwait(false);

        // system_views.disk_usage reports the sstables of each table (Cassandra 5); data still in memtables is not in it.
        var rows = await session.ExecuteAsync(new SimpleStatement($"SELECT table_name, mebibytes FROM system_views.disk_usage WHERE keyspace_name = '{keyspace}' ALLOW FILTERING")).ConfigureAwait(false);
        var tables = rows.Select(static r => new TableSize(r.GetValue<string>("table_name"), r.GetValue<long>("mebibytes") * 1024L * 1024L)).OrderByDescending(static t => t.Bytes).ToList();
        var release = (await session.ExecuteAsync(new SimpleStatement("SELECT release_version FROM system.local")).ConfigureAwait(false)).First().GetValue<string>("release_version");
        return (Describe(tables.Sum(static t => t.Bytes), effects, rounds, includesUnflushed: !flushed, tables), "Cassandra " + release);
    }

    private static StorageSizeResult Describe(long total, long effects, int rounds, bool includesUnflushed, List<TableSize> tables) =>
        new(total, effects, effects == 0 ? 0 : (double)total / effects, total / 1024.0 / rounds, includesUnflushed, tables);

    private static bool RunProcess(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true });
            process!.WaitForExit(TimeSpan.FromMinutes(2));
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
