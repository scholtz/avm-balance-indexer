using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Voi publishes no ledger deltas, so a window of recent rounds (which has real payments, application calls and inner
/// payments, unlike its first 1000 rounds) is checked against what the public indexer reports for the same rounds: per
/// account, the native effects of transactions must equal the net of the indexer's transactions.
/// It also exercises archival host failover, since many Voi gossip hosts lack blocks.
/// </summary>
public class VoiRecentRoundsTests(ITestOutputHelper output)
{
    private const int Window = 2500;
    private const ulong BehindTip = 5000;

    private static readonly EffectType[] TransactionEffects =
        [EffectType.Fee, EffectType.FeeCollected, EffectType.Payment, EffectType.CloseOut, EffectType.Reward];

    [LiveFact]
    public async Task Recent_Rounds_Match_The_Public_Indexer()
    {
        using var reference = ReferenceNode.Voi();
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = ChainId.Voi, MaxAttempts = 40, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);

        var first = await adapter.GetLatestRoundAsync(CancellationToken.None) - BehindTip;
        var last = first + Window - 1;
        var normalizer = new AlgoEffectNormalizer();
        using var gate = new SemaphoreSlim(16);
        var blocks = await Task.WhenAll(Enumerable.Range(0, Window + 1).Select(async i =>
        {
            await gate.WaitAsync();
            try
            {
                return await adapter.FetchBlockAsync(first - 1 + (ulong)i, CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }
        }));

        var ours = new Dictionary<Address, BigInteger>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < blocks.Length; i++)
        {
            foreach (var delta in normalizer.Normalize(blocks[i], RewardsState.FromHeader(blocks[i - 1].Block)))
            {
                if (!TransactionEffects.Contains(delta.Type))
                {
                    continue;
                }

                Add(ours, delta.Account, delta.Delta);
                if (delta.Id.TransactionId.Length > 0)
                {
                    ids.Add(delta.Id.TransactionId);
                }
            }
        }

        var transactions = await reference.TransactionsInRangeAsync(first, last);
        var theirs = new Dictionary<Address, BigInteger>();
        var topLevelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in transactions)
        {
            if (t.Id.Length > 0 && (t.Fee > 0 || t.Amount > 0 || t.ClosingAmount > 0))
            {
                topLevelIds.Add(t.Id);
            }

            Add(theirs, t.Sender, -(BigInteger)t.Fee - t.Amount - t.ClosingAmount + t.SenderRewards);
            if (t.Receiver is { } receiver)
            {
                Add(theirs, receiver, (BigInteger)t.Amount + t.ReceiverRewards);
            }

            if (t.CloseTo is { } closeTo)
            {
                Add(theirs, closeTo, (BigInteger)t.ClosingAmount + t.CloseRewards);
            }
        }

        // The indexer does not list the fee sink credit, so add it from the fees to compare like with like.
        var feeSink = new Address(blocks[1].Block.FeeSink!.Bytes);
        Add(theirs, feeSink, transactions.Aggregate(BigInteger.Zero, static (sum, t) => sum + t.Fee));

        var payments = transactions.Count(static t => t.Amount > 0);
        output.WriteLine($"voi rounds {first}-{last}: {transactions.Count} indexer transactions, {payments} payments, {ours.Count} accounts with effects");
        Assert.True(payments > 0, "The window has no payments, so it proves nothing.");

        var problems = new List<string>();
        foreach (var account in ours.Keys.Union(theirs.Keys))
        {
            var indexed = ours.GetValueOrDefault(account);
            var reported = theirs.GetValueOrDefault(account);
            if (indexed != reported)
            {
                problems.Add($"{ReferenceNode.Encode(account)}: indexed {indexed} but indexer says {reported}");
            }
        }

        Assert.True(problems.Count == 0, $"{problems.Count} accounts differ:{Environment.NewLine}{string.Join(Environment.NewLine, problems.Take(20))}");
        Assert.True(topLevelIds.IsSubsetOf(ids), $"Transactions missing from the index: {string.Join(", ", topLevelIds.Except(ids).Take(10))}");
    }

    private static void Add(Dictionary<Address, BigInteger> balances, Address account, BigInteger delta) =>
        balances[account] = balances.GetValueOrDefault(account) + delta;
}
