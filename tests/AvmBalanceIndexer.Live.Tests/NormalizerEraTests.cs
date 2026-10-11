using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Blocks of every consensus era go through the whole normalizer (payments, assets, applications, logs, inner transactions). A
/// protocol detail the code does not know about, such as the missing created asset id of the early protocols, throws here.
/// </summary>
public class NormalizerEraTests(ITestOutputHelper output)
{
    private static readonly ulong[] Samples =
        [1_000_000, 3_000_000, 6_000_000, 8_874_561, 9_000_000, 10_000_000, 12_000_000, 13_500_000, 15_000_000, 17_000_000, 18_000_000, 19_500_000, 21_000_000, 24_000_000, 28_000_000, 32_000_000, 36_000_000, 40_000_000, 45_000_000, 50_000_000, 55_000_000, 60_000_000, 65_000_000];

    [LiveFact]
    public Task MainNet_Blocks_Of_Every_Era_Normalize() => AssertSamplesAsync(ChainId.AlgorandMainnet, ReferenceNode.MainNet());

    [LiveFact]
    public Task TestNet_Blocks_Of_Every_Era_Normalize() => AssertSamplesAsync(ChainId.AlgorandTestnet, ReferenceNode.TestNet());

    private async Task AssertSamplesAsync(string chain, ReferenceNode reference)
    {
        using var _ = reference;
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = chain, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);
        var normalizer = new AlgoEffectNormalizer();
        var latest = await adapter.GetLatestRoundAsync(CancellationToken.None);

        var effects = 0;
        var transactions = 0;
        foreach (var round in Samples.Where(r => r < latest).Append(latest - 20))
        {
            var block = await adapter.FetchBlockAsync(round, CancellationToken.None);
            var previous = await adapter.FetchBlockAsync(round - 1, CancellationToken.None);

            var normalized = normalizer.NormalizeBlock(block, RewardsState.FromHeader(previous.Block), await LiveAssetDirectory.LoadAsync(reference, adapter, block));

            Assert.Equal(Enumerable.Range(0, normalized.Deltas.Count), normalized.Deltas.Select(static d => d.Id.Ordinal));
            effects += normalized.Deltas.Count;
            transactions += block.ApplyData.Count;
        }

        output.WriteLine($"{chain}: {transactions} transactions normalized into {effects} effects");
        Assert.True(effects > 0);
    }
}
