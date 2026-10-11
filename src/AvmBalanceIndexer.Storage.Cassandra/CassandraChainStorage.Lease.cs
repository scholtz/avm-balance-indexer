using AvmBalanceIndexer.Domain.Storage;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage : IWriterLeaseProvider
{
    /// <summary>
    /// The lease is a row with a TTL that its owner renews with a conditional update. A crashed owner stops renewing and the row
    /// expires, so a standby can take over after at most one TTL. The owner gives up on its own after 80% of the TTL without a
    /// successful renewal, before the row can expire under it.
    /// </summary>
    public async Task<IWriterLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var owner = Guid.NewGuid().ToString("N");
        var statement = await BindAsync(
            $"INSERT INTO {Table("writer_lease")} (chain, owner) VALUES (?, ?) IF NOT EXISTS USING TTL ?",
            cancellationToken,
            _chain,
            owner,
            (int)_leaseTtl.TotalSeconds).ConfigureAwait(false);
        statement.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
        var result = await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false);
        return result.First().GetValue<bool>("[applied]") ? new Lease(this, owner) : null;
    }

    private sealed class Lease : IWriterLease
    {
        private readonly CassandraChainStorage _storage;
        private readonly string _owner;
        private readonly CancellationTokenSource _lost = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _renewal;

        public Lease(CassandraChainStorage storage, string owner)
        {
            _storage = storage;
            _owner = owner;
            _renewal = RenewAsync();
        }

        public CancellationToken Lost => _lost.Token;

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await _renewal.ConfigureAwait(false);

            if (!_lost.IsCancellationRequested)
            {
                try
                {
                    var statement = await _storage.BindAsync(
                        $"DELETE FROM {_storage.Table("writer_lease")} WHERE chain = ? IF owner = ?",
                        CancellationToken.None,
                        _storage._chain,
                        _owner).ConfigureAwait(false);
                    statement.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
                    await _storage.ExecuteAsync(statement, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is DriverException or TimeoutException)
                {
                    // The row expires by itself within one TTL.
                }
            }

            _lost.Dispose();
            _stop.Dispose();
        }

        private async Task RenewAsync()
        {
            var ttl = _storage._leaseTtl;
            var lastRenewal = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var timer = new PeriodicTimer(ttl / 3);
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    try
                    {
                        var statement = await _storage.BindAsync(
                            $"UPDATE {_storage.Table("writer_lease")} USING TTL ? SET owner = ? WHERE chain = ? IF owner = ?",
                            _stop.Token,
                            (int)ttl.TotalSeconds,
                            _owner,
                            _storage._chain,
                            _owner).ConfigureAwait(false);
                        statement.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
                        var result = await _storage.ExecuteAsync(statement, _stop.Token).ConfigureAwait(false);
                        if (!result.First().GetValue<bool>("[applied]"))
                        {
                            // Someone else owns the row (or it expired under us): the lease is gone.
                            await _lost.CancelAsync().ConfigureAwait(false);
                            return;
                        }

                        lastRenewal.Restart();
                    }
                    catch (Exception ex) when (ex is DriverException or TimeoutException)
                    {
                        // Unknown outcome: keep trying until the row could have expired.
                        if (lastRenewal.Elapsed > ttl * 0.8)
                        {
                            await _lost.CancelAsync().ConfigureAwait(false);
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Disposed.
            }
        }
    }
}
