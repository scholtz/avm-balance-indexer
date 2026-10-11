using System.Globalization;
using Npgsql;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage
{
    // Serializes concurrent schema changes from several pods; any constant works as long as it is unique to this tool.
    private const long SchemaLockKey = 0x41564D5F42414C21;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        foreach (var script in ReadEmbeddedScripts())
        {
            await ExecuteAsync(connection, transaction, script, cancellationToken, unlimited: true).ConfigureAwait(false);
        }

        await EnsurePartitionCountMatchesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        for (var remainder = 0; remainder < _accountPartitions; remainder++)
        {
            // Numbers only: the statement text contains no caller-supplied strings.
            var statement = string.Create(
                CultureInfo.InvariantCulture,
                $"CREATE TABLE IF NOT EXISTS effects_by_account_token_p{remainder} PARTITION OF effects_by_account_token FOR VALUES WITH (MODULUS {_accountPartitions}, REMAINDER {remainder})");
            await ExecuteAsync(connection, transaction, statement, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The number of hash partitions is part of the data layout and cannot change without a rebuild. Fail with a clear message
    /// instead of letting PostgreSQL reject the overlapping partitions.
    /// </summary>
    private async Task EnsurePartitionCountMatchesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_inherits WHERE inhparent = 'effects_by_account_token'::regclass", connection, transaction);
        var existing = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (existing != 0 && existing != _accountPartitions)
        {
            throw new InvalidOperationException(
                $"The database was created with {existing} account hash partitions but Storage:PostgreSql:AccountHashPartitions is {_accountPartitions}. The setting is fixed per database; restore the original value or reindex into a new database.");
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, statement, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        lock (_knownBuckets)
        {
            _knownBuckets.Add(bucket);
        }
    }

    /// <summary>
    /// Takes the schema lock for the rest of the transaction. A transaction-scoped lock is released by commit or rollback
    /// (including a broken connection), so there is nothing to release in a finally block that could hide the real error.
    /// </summary>
    private static async Task LockSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
        command.Parameters.AddWithValue("key", SchemaLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, bool unlimited = false)
    {
        // Embedded scripts may copy a lot of data (the holdings_by_token backfill), so they are not subject to the command timeout.
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        if (unlimited)
        {
            command.CommandTimeout = 0;
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
