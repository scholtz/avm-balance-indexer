using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.TestSupport;

public partial class InMemoryChainStorage : IWriterLeaseProvider, IStorageFailureClassifier
{
    private readonly object _leaseGate = new();
    private InMemoryLease? _lease;

    /// <summary>Number of leases handed out so far.</summary>
    public int LeasesGranted { get; private set; }

    public virtual Task<IWriterLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        lock (_leaseGate)
        {
            if (_lease is not null)
            {
                return Task.FromResult<IWriterLease?>(null);
            }

            LeasesGranted++;
            return Task.FromResult<IWriterLease?>(_lease = new InMemoryLease(this));
        }
    }

    /// <summary>Simulates losing the lease (session death, expiry): the holder is told, and the lease is free again.</summary>
    public void RevokeLease()
    {
        InMemoryLease? lease;
        lock (_leaseGate)
        {
            lease = _lease;
            _lease = null;
        }

        lease?.MarkLost();
    }

    /// <summary>The reference storage has no failures of its own.</summary>
    public bool IsTransient(Exception exception) => false;

    private void Release(InMemoryLease lease)
    {
        lock (_leaseGate)
        {
            if (ReferenceEquals(_lease, lease))
            {
                _lease = null;
            }
        }
    }

    private sealed class InMemoryLease(InMemoryChainStorage owner) : IWriterLease
    {
        private readonly CancellationTokenSource _lost = new();

        public CancellationToken Lost => _lost.Token;

        public void MarkLost() => _lost.Cancel();

        public ValueTask DisposeAsync()
        {
            owner.Release(this);
            return ValueTask.CompletedTask;
        }
    }
}
