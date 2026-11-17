using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Api;

/// <summary>The newest writer term for the status endpoint, remembered for a minute so that polling the status adds no query per request.</summary>
public sealed partial class IndexedByLookup(IChainStorage storage, TimeProvider clock, ILogger<IndexedByLookup>? logger = null) : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Snapshot? _snapshot;

    /// <summary>The newest term, or null when none is recorded or the log cannot be read (the status never fails because of it).</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<IndexedByInfo?> GetAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is { } cached && clock.GetUtcNow() < cached.ValidUntil)
        {
            return cached.Value;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot is { } fresh && clock.GetUtcNow() < fresh.ValidUntil)
            {
                return fresh.Value;
            }

            var now = clock.GetUtcNow();
            try
            {
                var terms = await storage.ListAsync(cancellationToken).ConfigureAwait(false);
                var value = terms is [var first, ..] ? new IndexedByInfo(first.Version, first.FromRound, first.StartedAt) : null;
                _snapshot = new Snapshot(value, now + Lifetime);
                return value;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
            {
                // An API that is upgraded before the worker has no table yet, and a storage timeout surfaces as a cancellation that is not
                // the request's: the status is still worth serving. A failure is remembered only briefly, so a blip does not hide the term.
                if (logger is not null)
                {
                    LogUnreadable(logger, ex);
                }

                _snapshot = new Snapshot(null, now + FailureLifetime);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    [LoggerMessage(EventId = 1410, Level = LogLevel.Warning, Message = "The indexing terms could not be read; the status reports no indexedBy for a few seconds")]
    private static partial void LogUnreadable(ILogger logger, Exception exception);

    private sealed record Snapshot(IndexedByInfo? Value, DateTimeOffset ValidUntil);
}
