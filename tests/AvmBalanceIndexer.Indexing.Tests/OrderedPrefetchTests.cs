using AvmBalanceIndexer.Indexing.Ingestion;

namespace AvmBalanceIndexer.Indexing.Tests;

public class OrderedPrefetchTests
{
    [Fact]
    public async Task Yields_Results_In_Round_Order_Even_When_Fetches_Finish_Out_Of_Order()
    {
        static async Task<ulong> Fetch(ulong round, CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromMilliseconds((10 - (int)round) * 5), token);
            return round;
        }

        var seen = new List<ulong>();
        await foreach (var round in OrderedPrefetch.StreamAsync(1, 10, 4, Fetch, CancellationToken.None))
        {
            seen.Add(round);
        }

        Assert.Equal(Enumerable.Range(1, 10).Select(static i => (ulong)i), seen);
    }

    [Fact]
    public async Task Never_Exceeds_The_Concurrency_Limit()
    {
        var inFlight = 0;
        var peak = 0;

        async Task<ulong> Fetch(ulong round, CancellationToken token)
        {
            var now = Interlocked.Increment(ref inFlight);
            int observed;
            while ((observed = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, observed) != observed)
            {
            }

            await Task.Delay(10, token);
            Interlocked.Decrement(ref inFlight);
            return round;
        }

        await foreach (var _ in OrderedPrefetch.StreamAsync(1, 40, 3, Fetch, CancellationToken.None))
        {
        }

        Assert.InRange(peak, 1, 3);
    }

    [Fact]
    public async Task Empty_Range_Yields_Nothing_And_Fetches_Nothing()
    {
        var calls = 0;

        await foreach (var _ in OrderedPrefetch.StreamAsync(
            5,
            4,
            4,
            (round, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(round);
            },
            CancellationToken.None))
        {
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_Failed_Fetch_Surfaces_At_Its_Position_And_Cancels_The_Rest()
    {
        var cancelled = 0;

        async Task<ulong> Fetch(ulong round, CancellationToken token)
        {
            if (round == 3)
            {
                throw new InvalidOperationException("boom");
            }

            try
            {
                await Task.Delay(round == 4 ? TimeSpan.FromSeconds(30) : TimeSpan.Zero, token);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelled);
                throw;
            }

            return round;
        }

        var seen = new List<ulong>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var round in OrderedPrefetch.StreamAsync(1, 10, 4, Fetch, CancellationToken.None))
            {
                seen.Add(round);
            }
        });

        Assert.Equal([1UL, 2UL], seen);
        Assert.True(cancelled >= 1);
    }

    [Fact]
    public async Task Single_Round_Range_Works_At_The_Upper_Bound_Of_Ulong()
    {
        var seen = new List<ulong>();
        await foreach (var round in OrderedPrefetch.StreamAsync(ulong.MaxValue, ulong.MaxValue, 2, (r, _) => Task.FromResult(r), CancellationToken.None))
        {
            seen.Add(round);
        }

        Assert.Equal([ulong.MaxValue], seen);
    }
}
