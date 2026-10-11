using System.Diagnostics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Measures where backfill time goes: block download, decode and normalization, and the database commit. Only runs when
/// <c>AVM_BENCH=1</c> is set (it downloads thousands of blocks), and prints numbers instead of asserting them:
/// <c>AVM_BENCH=1 dotnet test tests/AvmBalanceIndexer.Live.Tests --filter BackfillBenchmark -v n</c>.
/// </summary>
[Collection(PostgresServerDefinition.Name)]
public class BackfillBenchmark(PostgresFixture postgres, ITestOutputHelper output)
{
    private const int Blocks = 1500;

    [BenchmarkFact]
    public async Task Where_Does_The_Time_Go()
    {
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = ChainId.AlgorandMainnet, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var inner = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);

        foreach (var concurrency in new[] { 64, 256 })
        {
            // Early blocks are tiny, so this isolates request latency and host behaviour from payload size.
            var started = Stopwatch.StartNew();
            var early = 3_000_000UL + (ulong)(concurrency * 5_000);
            await foreach (var _ in OrderedPrefetch.StreamAsync(early, early + 1_199, concurrency, inner.FetchBlockAsync, CancellationToken.None))
            {
            }

            output.WriteLine($"early (small) blocks, concurrency {concurrency}: {1200 / started.Elapsed.TotalSeconds:F0} rounds/s");
        }

        foreach (var concurrency in new[] { 8, 32, 96, 192 })
        {
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var fetch = Stopwatch.StartNew();
            var fetched = 0L;
            await foreach (var block in OrderedPrefetch.StreamAsync(60_000_000UL + (ulong)(concurrency * 10_000), 60_000_000UL + (ulong)(concurrency * 10_000) + 599, concurrency, inner.FetchBlockAsync, CancellationToken.None))
            {
                fetched += block.ApplyData.Count;
            }

            var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;
            output.WriteLine($"download+decode, concurrency {concurrency}: {600 / fetch.Elapsed.TotalSeconds:F0} rounds/s ({fetched} transactions), {cpu.TotalMilliseconds / 600:F1} ms CPU per round, {cpu.TotalSeconds / fetch.Elapsed.TotalSeconds:F1} cores busy of {Environment.ProcessorCount}");
        }

        var cached = new CachedChainAdapter(inner);
        const ulong first = 64_000_000;
        var blocks = new List<BlockData>();
        await foreach (var block in OrderedPrefetch.StreamAsync(first - 1, first + Blocks - 1, 64, cached.FetchBlockAsync, CancellationToken.None))
        {
            blocks.Add(block);
        }

        var normalizer = new AlgoEffectNormalizer();
        var normalize = Stopwatch.StartNew();
        var effects = 0;
        var skipped = 0;
        for (var i = 1; i < blocks.Count; i++)
        {
            try
            {
                effects += normalizer.Normalize(blocks[i], RewardsState.FromHeader(blocks[i - 1].Block)).Count;
            }
            catch (InvalidDataException)
            {
                skipped++; // destroys an asset created before this window
            }
        }

        output.WriteLine($"normalize only: {(blocks.Count - 1) / normalize.Elapsed.TotalSeconds:F0} rounds/s, {effects / (double)(blocks.Count - 1 - skipped):F1} effects per round ({skipped} blocks skipped)");

        // The write path needs a chain history from genesis; measure it with the first rounds of the chain, whose blocks are empty
        // (one pool effect per round), which is what most of a backfill looks like, and with the busy window against fresh storage.
        foreach (var chain in new[] { ChainId.AlgorandTestnet })
        {
                        var testnet = AlgorandChainFactory.Create(
                new AlgorandChainOptions { Chain = chain, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) },
                http,
                NullLoggerFactory.Instance);
            var warm = new CachedChainAdapter(testnet);
            await foreach (var _ in OrderedPrefetch.StreamAsync(1, 3000, 64, warm.FetchBlockAsync, CancellationToken.None))
            {
            }

            foreach (var batch in new[] { 1, 25, 100 })
            {
                var target = await postgres.CreateStorageAsync(chain);
                var indexer = new RoundIndexer(warm, new AlgoEffectNormalizer(), target, Options.Create(new IndexerOptions { FetchConcurrency = 8, CommitBatchRounds = batch }), NullLogger<RoundIndexer>.Instance);
                var write = Stopwatch.StartNew();
                var result = await indexer.RunAsync(3000, CancellationToken.None);
                output.WriteLine($"{chain} blocks cached, normalize + track + commit to PostgreSQL, {batch} rounds per commit: {result.RoundsIndexed / write.Elapsed.TotalSeconds:F0} rounds/s ({result.EffectsWritten} effects)");
            }
        }
    }
}
