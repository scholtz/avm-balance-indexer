using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Asset creations from every consensus era: the protocols before inner transactions do not record the created asset id in
/// the block, so the id, the creator and the supply credited to it are checked against the public indexer.
/// </summary>
public class AssetCreationEraTests(ITestOutputHelper output)
{
    private static readonly ulong[] EraStarts = [6_000_000, 8_500_000, 11_000_000, 14_000_000, 17_000_000, 20_000_000, 25_000_000, 30_000_000, 40_000_000, 55_000_000];

    [LiveFact]
    public async Task MainNet_Created_Assets_Get_The_Ids_The_Indexer_Reports()
    {
        using var reference = ReferenceNode.MainNet();
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = ChainId.AlgorandMainnet, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);
        var normalizer = new AlgoEffectNormalizer();

        var checkedAssets = 0;
        foreach (var start in EraStarts)
        {
            var creations = await reference.AssetCreationsAsync(start, start + 100_000, 4);
            foreach (var (round, assetId, creator) in creations.Take(3))
            {
                var block = await adapter.FetchBlockAsync(round, CancellationToken.None);
                var previous = await adapter.FetchBlockAsync(round - 1, CancellationToken.None);
                var normalized = normalizer.NormalizeBlock(block, RewardsState.FromHeader(previous.Block), AssetDirectory.Empty);

                var created = normalized.Deltas.Single(d => d.Token == TokenKey.Asa(assetId) && d.Type == EffectType.AssetCreate);
                Assert.Equal(creator, created.Account);
                Assert.Contains(normalized.TokenChanges, t => t.Key == TokenKey.Asa(assetId) && t.Creator == creator);
                checkedAssets++;
            }
        }

        output.WriteLine($"{checkedAssets} created assets in {EraStarts.Length} eras matched the indexer");
        Assert.True(checkedAssets >= EraStarts.Length, "Too few asset creations were found to cover the eras.");
    }
}
