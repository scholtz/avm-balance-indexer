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
/// The ledger audit (ADR 0021) against the real node: the real delta source and the audit comparison over rounds that were indexed from
/// genesis must find no mismatch, so a false alarm (which would stop production indexing) is caught here and not in a deployment.
/// </summary>
public class LedgerAuditLiveTests(ITestOutputHelper output)
{
    private const ulong Rounds = 150;

    [LiveFact]
    public Task TestNet_Early_Rounds_Agree_With_The_Ledger() => AssertNoMismatchAsync(ChainId.AlgorandTestnet, "https://testnet-api.4160.nodely.dev");

    [LiveFact]
    public Task MainNet_Early_Rounds_Agree_With_The_Ledger() => AssertNoMismatchAsync(ChainId.AlgorandMainnet, "https://mainnet-api.4160.nodely.dev");

    private async Task AssertNoMismatchAsync(string chain, string algodUrl)
    {
        var options = new AlgorandChainOptions { Chain = chain, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, AlgorandChainFactory.CreateBlockHttpClient(), NullLoggerFactory.Instance);
        var storage = new InMemoryChainStorage(new ChainContext(chain));
        await new RoundIndexer(adapter, new AlgoEffectNormalizer(), storage, Options.Create(new IndexerOptions { FetchConcurrency = 4 }), NullLogger<RoundIndexer>.Instance)
            .RunAsync(Rounds, CancellationToken.None);

        using var source = new AlgodLedgerDeltaSource(new HttpClient { BaseAddress = new Uri(algodUrl + "/"), Timeout = TimeSpan.FromSeconds(60) }, NullLogger<AlgodLedgerDeltaSource>.Instance, ownsClient: true);
        var audited = 0;
        var compared = 0;
        var problems = new List<string>();
        for (ulong round = 1; round <= Rounds; round++)
        {
            var delta = await source.TryGetAsync(round, CancellationToken.None);
            if (delta is null)
            {
                continue;
            }

            audited++;
            compared += delta.Algo.Count + delta.Assets.Count;
            problems.AddRange((await LedgerAudit.CompareAsync(storage, delta, round, 8, CancellationToken.None)).Select(m => $"round {round}: {m.Account} {m.Token} indexed {m.Indexed} ledger {m.Ledger}"));
        }

        output.WriteLine($"{chain}: {audited} rounds audited, {compared} balances compared, {problems.Count} mismatches");
        Assert.True(audited > 0, "The node served no ledger deltas, so the test proves nothing.");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
    }
}
