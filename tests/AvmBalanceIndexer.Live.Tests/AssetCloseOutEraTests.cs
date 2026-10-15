using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Asset close-outs (<c>aclose</c>) of two protocol eras (issue #115). Early blocks do not record the closed amount (no
/// <c>aca</c> in the apply data), so the TestNet indexer credited nothing to the destination and later stopped on a balance
/// underflow at round 4982989. Later blocks state the amount.
/// </summary>
public class AssetCloseOutEraTests
{
    private const ulong AssetOfTheStall = 127647;

    private static async Task<(NormalizedBlock Block, ReferenceNode Reference)> NormalizeAsync(ulong round)
    {
        var reference = ReferenceNode.TestNet();
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = ChainId.AlgorandTestnet, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);
        var block = await adapter.FetchBlockAsync(round, CancellationToken.None);
        var previous = await adapter.FetchBlockAsync(round - 1, CancellationToken.None);
        var normalized = new AlgoEffectNormalizer().NormalizeBlock(block, RewardsState.FromHeader(previous.Block), await LiveAssetDirectory.LoadAsync(reference, adapter, block));
        return (normalized, reference);
    }

    [LiveFact]
    public async Task TestNet_Early_Close_Outs_Without_An_Amount_Are_Resolved_From_The_Running_Balance()
    {
        // The closer BERLDH... closes asset 127647 to CQRMTAHZ... (the account whose later transfer made the worker stop).
        var (block, reference) = await NormalizeAsync(4_848_388);
        using var _ = reference;

        var closing = block.Deltas.Where(static d => d.Token == TokenKey.Asa(AssetOfTheStall) && d.CloseOut != CloseOutRole.None).ToList();

        Assert.Equal([CloseOutRole.Debit, CloseOutRole.Credit], closing.Select(static d => d.CloseOut));
        Assert.StartsWith("BERLDH", ReferenceNode.Encode(closing[0].Account), StringComparison.Ordinal);
        Assert.StartsWith("CQRMTAHZ", ReferenceNode.Encode(closing[1].Account), StringComparison.Ordinal);
        Assert.Equal(closing[1].Account, closing[0].Counterparty);
        Assert.Equal(closing[0].Account, closing[1].Counterparty);
    }

    [LiveFact]
    public async Task TestNet_Later_Close_Outs_State_The_Amount_And_Stay_Exact()
    {
        var (block, reference) = await NormalizeAsync(15_000_054);
        using var _ = reference;

        var closing = block.Deltas.Where(static d => d.Type == EffectType.AssetOptOut && d.Delta.Sign < 0).ToList();

        var debit = Assert.Single(closing);
        Assert.Equal(CloseOutRole.None, debit.CloseOut);
        Assert.Equal(-10_000, (long)debit.Delta);
        Assert.Contains(block.Deltas, d => d.Account == debit.Counterparty && d.Token == debit.Token && d.Delta == 10_000 && d.CloseOut == CloseOutRole.None);
    }
}
