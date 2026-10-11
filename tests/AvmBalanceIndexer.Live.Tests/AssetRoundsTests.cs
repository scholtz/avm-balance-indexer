using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Checks ASA effects of a window of recent MainNet rounds (busy with transfers, opt-ins, closes and inner transfers) against
/// the ledger's own post-round asset holdings. Starting balances are unknown mid-chain, so the oracle is relative: between two
/// rounds in which the ledger reports the same (account, asset), the holding difference must equal the sum of our effects.
/// A holding the ledger reports as removed counts as zero. An account's first appearance in the window has no baseline.
/// </summary>
public class AssetRoundsTests(ITestOutputHelper output)
{
    private const int Window = 120;
    private const ulong BehindTip = 30000;

    [LiveFact]
    public async Task MainNet_Recent_Asset_Effects_Match_The_Ledger()
    {
        using var reference = ReferenceNode.MainNet();
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = ChainId.AlgorandMainnet, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);

        var first = await adapter.GetLatestRoundAsync(CancellationToken.None) - BehindTip;
        var last = first + Window - 1;
        var normalizer = new AlgoEffectNormalizer();

        var blocks = new Dictionary<ulong, BlockData>();
        for (var round = first - 1; round <= last; round++)
        {
            blocks[round] = await adapter.FetchBlockAsync(round, CancellationToken.None);
        }

        var sums = new Dictionary<ulong, Dictionary<(Address, ulong), BigInteger>>();
        var ledger = new Dictionary<ulong, IReadOnlyDictionary<(Address Account, ulong AssetId), ulong>>();
        var kinds = new HashSet<EffectType>();
        for (var round = first; round <= last; round++)
        {
            var normalized = normalizer.NormalizeBlock(blocks[round], RewardsState.FromHeader(blocks[round - 1].Block), await DirectoryAsync(reference, adapter, blocks[round]));
            var perAccount = new Dictionary<(Address, ulong), BigInteger>();
            foreach (var delta in normalized.Deltas.Where(static d => d.Token.Kind == TokenKind.Asa))
            {
                kinds.Add(delta.Type);
                var key = (delta.Account, delta.Token.TokenId);
                perAccount[key] = perAccount.GetValueOrDefault(key) + delta.Delta;
            }

            sums[round] = perAccount;
            ledger[round] = await reference.LedgerAssetHoldingsAfterAsync(round);
        }

        var lastSeen = new Dictionary<(Address, ulong), (ulong Round, ulong Amount)>();
        var checkedHoldings = 0;
        var problems = new List<string>();
        for (var round = first; round <= last; round++)
        {
            foreach (var (key, amount) in ledger[round])
            {
                if (lastSeen.TryGetValue(key, out var seen))
                {
                    var expected = (BigInteger)amount - seen.Amount;
                    var ours = Enumerable.Range(0, (int)(round - seen.Round)).Aggregate(BigInteger.Zero, (sum, i) => sum + sums[seen.Round + 1 + (ulong)i].GetValueOrDefault(key));
                    checkedHoldings++;
                    if (ours != expected)
                    {
                        problems.Add($"round {round} {ReferenceNode.Encode(key.Item1)} asset {key.Item2}: ledger changed by {expected} since round {seen.Round} but we by {ours}");
                    }
                }

                lastSeen[key] = (round, amount);
            }

            foreach (var (key, sum) in sums[round].Where(kv => !kv.Value.IsZero && !ledger[round].ContainsKey(kv.Key)))
            {
                problems.Add($"round {round} {ReferenceNode.Encode(key.Item1)} asset {key.Item2}: we changed the balance by {sum} but the ledger did not");
            }
        }

        output.WriteLine($"mainnet rounds {first}-{last}: {checkedHoldings} holdings checked, effect types {string.Join(", ", kinds.Order())}, {problems.Count} problems");
        foreach (var problem in problems)
        {
            output.WriteLine(problem);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
        Assert.True(checkedHoldings > 0, "No holding could be compared.");
        Assert.Contains(EffectType.AssetTransfer, kinds);
    }

    private static async Task<AssetDirectory> DirectoryAsync(ReferenceNode reference, AlgorandChainAdapter adapter, BlockData block)
    {
        var records = new List<TokenInfo>();
        foreach (var assetId in AssetScanner.DestroyedAssets(block))
        {
            // The indexer reports a destroyed asset with total 0, so the supply is read from the creating transaction.
            var (creator, createdAt) = await reference.AssetAsync(assetId);
            var creation = await adapter.FetchBlockAsync(createdAt, CancellationToken.None);
            var parameters = FindCreation(creation.ApplyData, assetId) ?? throw new InvalidOperationException($"Asset {assetId} was not created in round {createdAt}.");
            records.Add(AssetRecords.Created(assetId, creator, parameters, createdAt));
        }

        return new AssetDirectory(records);
    }

    private static RawAssetParams? FindCreation(IEnumerable<TxnApplyData> transactions, ulong assetId)
    {
        foreach (var apply in transactions)
        {
            if (apply.CreatedAssetId == assetId)
            {
                return RawTransaction.Read(apply.RawTxn).Params;
            }

            if (FindCreation(apply.InnerTxns, assetId) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }
}
