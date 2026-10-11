using System.Diagnostics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// The Cassandra counterpart of the write-path part of <see cref="BackfillBenchmark"/>: the first TestNet rounds are downloaded once,
/// then indexed into a fresh keyspace with different commit sizes. Only runs with <c>AVM_BENCH=1</c>:
/// <c>AVM_BENCH=1 dotnet test tests/AvmBalanceIndexer.Live.Tests --filter CassandraBackfillBenchmark -v n</c>.
/// </summary>
[Collection(CassandraServerDefinition.Name)]
public class CassandraBackfillBenchmark(CassandraFixture cassandra, ITestOutputHelper output)
{
    private const ulong Rounds = 3000;

    [BenchmarkFact]
    public async Task Where_Does_The_Write_Time_Go()
    {
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var testnet = AlgorandChainFactory.Create(
            new AlgorandChainOptions { Chain = ChainId.AlgorandTestnet, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) },
            http,
            NullLoggerFactory.Instance);
        var warm = new CachedChainAdapter(testnet);
        await foreach (var _ in OrderedPrefetch.StreamAsync(1, Rounds, 64, warm.FetchBlockAsync, CancellationToken.None))
        {
        }

        foreach (var batch in new[] { 1, 25, 100 })
        {
            var target = await cassandra.CreateStorageAsync(ChainId.AlgorandTestnet);
            var indexer = new RoundIndexer(
                warm,
                new AlgoEffectNormalizer(),
                target,
                Options.Create(new IndexerOptions { FetchConcurrency = 8, CommitBatchRounds = batch }),
                NullLogger<RoundIndexer>.Instance);
            var write = Stopwatch.StartNew();
            var result = await indexer.RunAsync(Rounds, CancellationToken.None);
            output.WriteLine($"TestNet blocks cached, normalize + track + commit to Cassandra, {batch} rounds per commit: {result.RoundsIndexed / write.Elapsed.TotalSeconds:F0} rounds/s ({result.EffectsWritten} effects)");
        }
    }

    /// <summary>
    /// What the write path costs when rounds are busy, without any network: synthetic rounds of 400 payments between 2,000 accounts
    /// (800 effects each) written straight into a keyspace, one round per commit and 25 rounds per commit.
    /// </summary>
    [BenchmarkFact]
    public async Task How_Many_Effects_Per_Second_Can_Cassandra_Take()
    {
        foreach (var batch in new[] { 1, 25 })
        {
            var storage = await cassandra.CreateStorageAsync(ChainId.AlgorandTestnet);
            const int accounts = 2000;
            const int rounds = 100;
            const int paymentsPerRound = 400;
            var balances = new ulong[accounts];
            Array.Fill(balances, 1_000_000_000UL);

            var genesis = Enumerable.Range(0, accounts)
                .Select(i => new TokenEffect(new EffectId(0, 0, string.Empty, [], i), TestChain.Account(i), TokenKey.Native, EffectType.Genesis, balances[i], Amount.From(balances[i]), null))
                .ToArray();
            await storage.WriteRoundAsync(
                new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), null, IngestionState.Backfilling, DateTimeOffset.UnixEpoch),
                genesis,
                CancellationToken.None);

            var writes = new List<RoundWrite>();
            for (var round = 1; round <= rounds; round++)
            {
                var effects = new List<TokenEffect>(paymentsPerRound * 2);
                for (var n = 0; n < paymentsPerRound; n++)
                {
                    var from = (round * 7 + n * 13) % accounts;
                    var to = (round * 11 + n * 17 + 1) % accounts;
                    if (from == to)
                    {
                        to = (to + 1) % accounts;
                    }

                    balances[from] -= 10;
                    balances[to] += 10;
                    effects.Add(new TokenEffect(new EffectId((ulong)round, n + 1, $"TX{round}-{n}", [], effects.Count), TestChain.Account(from), TokenKey.Native, EffectType.Payment, -10, Amount.From(balances[from]), TestChain.Account(to)));
                    effects.Add(new TokenEffect(new EffectId((ulong)round, n + 1, $"TX{round}-{n}", [], effects.Count), TestChain.Account(to), TokenKey.Native, EffectType.Payment, 10, Amount.From(balances[to]), TestChain.Account(from)));
                }

                writes.Add(new RoundWrite(new ChainStatus(ChainId.AlgorandTestnet, (ulong)round, TestChain.FakeHash((ulong)round), null, IngestionState.Backfilling, DateTimeOffset.UnixEpoch), effects));
            }

            var clock = Stopwatch.StartNew();
            foreach (var chunk in writes.Chunk(batch))
            {
                await storage.WriteRoundsAsync(chunk, CancellationToken.None);
            }

            output.WriteLine($"synthetic busy rounds, {batch} rounds per commit: {rounds / clock.Elapsed.TotalSeconds:F1} rounds/s, {rounds * paymentsPerRound * 2 / clock.Elapsed.TotalSeconds:F0} effects/s");
        }
    }
}
