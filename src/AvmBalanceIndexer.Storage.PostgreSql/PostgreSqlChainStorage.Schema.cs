using System.Globalization;
using Npgsql;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage
{
    // Serializes concurrent bootstrap from several pods; any constant works as long as it is unique to this tool.
    private const long SchemaLockKey = 0x41564D5F42414C21;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, $"SELECT pg_advisory_lock({SchemaLockKey})", cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var script in ReadEmbeddedScripts())
            {
                await ExecuteAsync(connection, script, cancellationToken).ConfigureAwait(false);
            }

            for (var remainder = 0; remainder < _accountPartitions; remainder++)
            {
                // Numbers only: the statement text contains no caller-supplied strings.
                var statement = string.Create(
                    CultureInfo.InvariantCulture,
                    $"CREATE TABLE IF NOT EXISTS effects_by_account_token_p{remainder} PARTITION OF effects_by_account_token FOR VALUES WITH (MODULUS {_accountPartitions}, REMAINDER {remainder})");
                await ExecuteAsync(connection, statement, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await ExecuteAsync(connection, $"SELECT pg_advisory_unlock({SchemaLockKey})", CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Creates the range partition of the token-oriented effect table that holds a round bucket.</summary>
    private async Task EnsureBucketPartitionAsync(int bucket, CancellationToken cancellationToken)
    {
        lock (_knownBuckets)
        {
            if (_knownBuckets.Contains(bucket))
            {
                return;
            }
        }

        var statement = string.Create(
            CultureInfo.InvariantCulture,
            $"CREATE TABLE IF NOT EXISTS effects_by_token_bucket_b{bucket} PARTITION OF effects_by_token_bucket FOR VALUES FROM ({bucket}) TO ({bucket + 1})");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, $"SELECT pg_advisory_lock({SchemaLockKey})", cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, statement, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ExecuteAsync(connection, $"SELECT pg_advisory_unlock({SchemaLockKey})", CancellationToken.None).ConfigureAwait(false);
        }

        lock (_knownBuckets)
        {
            _knownBuckets.Add(bucket);
        }
    }

    private static IEnumerable<string> ReadEmbeddedScripts()
    {
        var assembly = typeof(PostgreSqlChainStorage).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(static n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded schema script {name} could not be opened.");
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
