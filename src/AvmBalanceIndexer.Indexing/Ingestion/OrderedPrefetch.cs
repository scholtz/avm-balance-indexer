using System.Runtime.CompilerServices;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>
/// Fetches a range of rounds with bounded parallelism and yields the results strictly in round order, so decoding
/// and network latency overlap while commits stay sequential.
/// </summary>
public static class OrderedPrefetch
{
    /// <summary>Streams <paramref name="fetch"/> results for rounds <paramref name="from"/> through <paramref name="to"/> inclusive.</summary>
    /// <param name="from">First round.</param>
    /// <param name="to">Last round (inclusive).</param>
    /// <param name="concurrency">Maximum number of in-flight fetches.</param>
    /// <param name="fetch">Fetches one round.</param>
    /// <param name="cancellationToken">Cancels outstanding fetches.</param>
    public static async IAsyncEnumerable<T> StreamAsync<T>(
        ulong from,
        ulong to,
        int concurrency,
        Func<ulong, CancellationToken, Task<T>> fetch,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var window = new Queue<Task<T>>();
        var next = from;
        var exhausted = from > to;

        try
        {
            while (true)
            {
                while (!exhausted && window.Count < concurrency)
                {
                    window.Enqueue(fetch(next, cts.Token));
                    exhausted = next == to;
                    next++;
                }

                if (window.Count == 0)
                {
                    yield break;
                }

                yield return await window.Dequeue().ConfigureAwait(false);
            }
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            foreach (var pending in window)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Outstanding fetches were abandoned on purpose; their failures are irrelevant once the stream ends.
                }
            }
        }
    }
}
