using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Api;

/// <summary>The newest writer term for the status endpoint, remembered for a minute so that polling the status adds no query per request.</summary>
public sealed class IndexedByLookup(IChainStorage storage, TimeProvider clock) : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IndexedByInfo? _value;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>The newest term, or null when none is recorded or the log cannot be read (the status never fails because of it).</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<IndexedByInfo?> GetAsync(CancellationToken cancellationToken)
    {
        if (clock.GetUtcNow() - _readAt < Lifetime && _readAt != DateTimeOffset.MinValue)
        {
            return _value;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = clock.GetUtcNow();
            if (_readAt != DateTimeOffset.MinValue && now - _readAt < Lifetime)
            {
                return _value;
            }

            try
            {
                var newest = (await storage.ListAsync(cancellationToken).ConfigureAwait(false)) is [var first, ..] ? first : null;
                _value = newest is null ? null : new IndexedByInfo(newest.Version, newest.FromRound, newest.StartedAt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                // An API that is upgraded before the worker has no table yet; the status is still worth serving. Tried again after the lifetime.
                _value = null;
            }

            _readAt = now;
            return _value;
        }
        finally
        {
            _gate.Release();
        }
    }
}
