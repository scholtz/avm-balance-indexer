using AvmBalanceIndexer.Domain.Storage;
using Npgsql;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public sealed partial class PostgreSqlChainStorage : IWriterLeaseProvider
{
    /// <summary>
    /// Takes a session-level advisory lock on a connection that stays open for as long as the lease is held. If the session dies
    /// (crash, network loss, failover) PostgreSQL releases the lock by itself, which is what makes failover automatic.
    /// </summary>
    public async Task<IWriterLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The key differs from the transaction-scoped writer lock of WriteRoundAsync (seed 1).
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@chain, 2))", connection);
            command.Parameters.AddWithValue("chain", _chain);
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true)
            {
                return new Lease(connection, _chain, _leaseHeartbeat);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Lease : IWriterLease
    {
        private readonly NpgsqlConnection _connection;
        private readonly string _chain;
        private readonly CancellationTokenSource _lost = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _heartbeat;

        public Lease(NpgsqlConnection connection, string chain, TimeSpan heartbeat)
        {
            _connection = connection;
            _chain = chain;
            _heartbeat = HeartbeatAsync(heartbeat);
        }

        public CancellationToken Lost => _lost.Token;

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await _heartbeat.ConfigureAwait(false);

            if (!_lost.IsCancellationRequested)
            {
                try
                {
                    await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@chain, 2))", _connection);
                    command.Parameters.AddWithValue("chain", _chain);
                    await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
                {
                    // The session may still own the lock; never hand it back to the pool.
                    NpgsqlConnection.ClearPool(_connection);
                }
            }

            await _connection.DisposeAsync().ConfigureAwait(false);
            _lost.Dispose();
            _stop.Dispose();
        }

        /// <summary>A lock lives and dies with its session, so a session that cannot answer means the lease is gone.</summary>
        private async Task HeartbeatAsync(TimeSpan interval)
        {
            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    await using var command = new NpgsqlCommand("SELECT 1", _connection) { CommandTimeout = Math.Max(1, (int)interval.TotalSeconds) };
                    await command.ExecuteScalarAsync(_stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Disposed.
            }
            catch (Exception)
            {
                await _lost.CancelAsync().ConfigureAwait(false);
                NpgsqlConnection.ClearPool(_connection);
            }
        }
    }
}
