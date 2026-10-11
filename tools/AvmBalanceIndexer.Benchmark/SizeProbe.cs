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
            WHERE c.relkind IN ('r', 'm')
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
        // Memtables are written out and the sstables compacted, so the size is the settled one and not that of a moment mid-compaction.
        var flushed = false;
        if (!string.IsNullOrWhiteSpace(container))
        {
            flushed = RunProcess("docker", $"exec {container} nodetool flush {keyspace}", out var flushOutput)
                && RunProcess("docker", $"exec {container} nodetool compact {keyspace}", out var compactOutput);
        }

        var cluster = Cluster.Builder().AddContactPoint(host).WithPort(port).WithLoadBalancingPolicy(new DCAwareRoundRobinPolicy(datacenter)).Build();
        using var clusterLifetime = cluster;
        var session = await cluster.ConnectAsync().ConfigureAwait(false);

        // Exact bytes from nodetool when it is reachable; otherwise system_views.disk_usage (Cassandra 5), which counts sstables only
        // and has whole-MiB resolution.
        List<TableSize>? tables = null;
        if (flushed && RunProcess("docker", $"exec {container} nodetool tablestats -F json {keyspace}", out var json))
        {
            tables = ParseTableStats(json, keyspace);
        }

        if (tables is null || tables.Count == 0)
        {
            flushed = false;
            var rows = await session.ExecuteAsync(new SimpleStatement($"SELECT table_name, mebibytes FROM system_views.disk_usage WHERE keyspace_name = '{keyspace}' ALLOW FILTERING")).ConfigureAwait(false);
            tables = [.. rows.Select(static r => new TableSize(r.GetValue<string>("table_name"), r.GetValue<long>("mebibytes") * 1024L * 1024L)).OrderByDescending(static t => t.Bytes)];
        }

        var release = (await session.ExecuteAsync(new SimpleStatement("SELECT release_version FROM system.local")).ConfigureAwait(false)).First().GetValue<string>("release_version");
        return (Describe(tables.Sum(static t => t.Bytes), effects, rounds, includesUnflushed: !flushed, tables), "Cassandra " + release);
    }

    private static StorageSizeResult Describe(long total, long effects, int rounds, bool includesUnflushed, List<TableSize> tables) =>
        new(total, effects, effects == 0 ? 0 : (double)total / effects, total / 1024.0 / rounds, includesUnflushed, tables);

    private static List<TableSize> ParseTableStats(string json, string keyspace)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var result = new List<TableSize>();
        if (document.RootElement.TryGetProperty(keyspace, out var space) && space.TryGetProperty("tables", out var tables))
        {
            foreach (var table in tables.EnumerateObject())
            {
                var bytes = table.Value.TryGetProperty("space_used_total", out var total) ? AsBytes(total)
                    : table.Value.TryGetProperty("space_used_live", out var live) ? AsBytes(live) : 0;
                result.Add(new TableSize(table.Name, bytes));
            }
        }

        return [.. result.OrderByDescending(static t => t.Bytes)];
    }

    // nodetool prints the byte counts as strings.
    private static long AsBytes(System.Text.Json.JsonElement value) =>
        value.ValueKind == System.Text.Json.JsonValueKind.String ? long.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : value.GetInt64();

    private static bool RunProcess(string file, string arguments, out string output)
    {
        output = string.Empty;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true });
            var error = process!.StandardError.ReadToEndAsync();
            var read = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            output = read.GetAwaiter().GetResult();
            _ = error.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
