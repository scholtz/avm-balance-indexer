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
        // SERIAL, not LOCAL_SERIAL: with several datacenters the lease must be exclusive across all of them.
        statement.SetSerialConsistencyLevel(ConsistencyLevel.Serial);
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
                    statement.SetSerialConsistencyLevel(ConsistencyLevel.Serial);
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
            var safe = ttl * 0.8;

            // The TTL runs from the moment a renewal is applied on the server, which is at the latest when the request was sent
            // back; measuring from the send time never overestimates how long the lease is still valid.
            var lastRenewalSent = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                using var timer = new PeriodicTimer(ttl / 3);
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    var sent = System.Diagnostics.Stopwatch.GetTimestamp();
                    var remaining = safe - System.Diagnostics.Stopwatch.GetElapsedTime(lastRenewalSent);
                    if (remaining <= TimeSpan.Zero)
                    {
                        await _lost.CancelAsync().ConfigureAwait(false);
                        return;
                    }

                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    attempt.CancelAfter(remaining);
                    try
                    {
                        var statement = await _storage.BindAsync(
                            $"UPDATE {_storage.Table("writer_lease")} USING TTL ? SET owner = ? WHERE chain = ? IF owner = ?",
                            attempt.Token,
                            (int)ttl.TotalSeconds,
                            _owner,
                            _storage._chain,
                            _owner).ConfigureAwait(false);
                        statement.SetSerialConsistencyLevel(ConsistencyLevel.Serial);
                        var result = await _storage.ExecuteAsync(statement, attempt.Token).ConfigureAwait(false);
                        if (!result.First().GetValue<bool>("[applied]"))
                        {
                            // Someone else owns the row (or it expired under us): the lease is gone.
                            await _lost.CancelAsync().ConfigureAwait(false);
                            return;
                        }

                        lastRenewalSent = sent;
                    }
                    catch (Exception) when (!_stop.IsCancellationRequested)
                    {
                        // Unknown outcome (driver error, timeout of this attempt, anything else): keep trying until the row could
                        // have expired; the check at the top of the loop then reports the lease lost.
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
