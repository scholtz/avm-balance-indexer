using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Real ARC-20 Smart ASAs (the reference contract names them SMART-ASA, unit S-ASA) are recognised from their creation block:
/// created by an application, starting frozen, with the application account as clawback. Plain assets of the same era stay plain.
/// </summary>
public class Arc20AssetsTests(ITestOutputHelper output)
{
    [LiveFact]
    public async Task MainNet_Smart_Asas_Are_Classified_From_Their_Creation_Block()
    {
        using var reference = ReferenceNode.MainNet();
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = ChainId.AlgorandMainnet, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);
        var normalizer = new AlgoEffectNormalizer();

        var smartAssets = await reference.AssetsByUnitAsync("S-ASA", 5);
        Assert.NotEmpty(smartAssets);
        foreach (var (assetId, createdAt, defaultFrozen, clawback) in smartAssets)
        {
            var token = await CreatedRecordAsync(adapter, normalizer, assetId, createdAt);

            output.WriteLine($"asset {assetId}: {token.Standard}, controlling application {token.ControllingAppId}");
            Assert.True(defaultFrozen);
            Assert.Equal(TokenStandard.Arc20, token.Standard);
            Assert.Equal(clawback, new Algorand.Address(ApplicationAddresses.Of(token.ControllingAppId!.Value).ToArray()).EncodeAsString());
        }

        // A well known plain asset (USDC) is created by a person's account, not by an application.
        var plain = await CreatedRecordAsync(adapter, normalizer, 31566704, 8874561);
        Assert.Equal((TokenStandard.Asa, (ulong?)null), (plain.Standard, plain.ControllingAppId));
    }

    private static async Task<TokenInfo> CreatedRecordAsync(AlgorandChainAdapter adapter, AlgoEffectNormalizer normalizer, ulong assetId, ulong round)
    {
        var block = await adapter.FetchBlockAsync(round, CancellationToken.None);
        var previous = await adapter.FetchBlockAsync(round - 1, CancellationToken.None);
        var normalized = normalizer.NormalizeBlock(block, RewardsState.FromHeader(previous.Block), AssetDirectory.Empty);
        return normalized.TokenChanges.Single(t => t.Key == TokenKey.Asa(assetId));
    }
}
