using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Checks the normalizer on a window of recent rounds, where proposer payouts and other implicit balance movements exist.
/// Starting balances are unknown mid-chain, so the oracle is relative: between two rounds in which the ledger reports the
/// same account, the ledger balance difference must equal the sum of our effects for that account in the rounds between.
/// An account's first appearance in the window has no baseline and is not checked, so a payout is only verified for a proposer
/// the ledger reported earlier in the window (the fee sink, debited by every payout, is always verified).
/// </summary>
public class PayoutEraRoundsTests(ITestOutputHelper output)
{
    private const int Window = 100;
    private const ulong BehindTip = 5000;

    [LiveFact]
    public Task MainNet_Recent_Rounds_Match_The_Ledger() => AssertWindowAsync(ChainId.AlgorandMainnet, ReferenceNode.MainNet(), expectPayouts: true);

    [LiveFact]
    public Task TestNet_Recent_Rounds_Match_The_Ledger() => AssertWindowAsync(ChainId.AlgorandTestnet, ReferenceNode.TestNet());

    private async Task AssertWindowAsync(string chain, ReferenceNode reference, bool expectPayouts = false)
    {
        using var _ = reference;
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = chain, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);

        var first = await adapter.GetLatestRoundAsync(CancellationToken.None) - BehindTip;
        var last = first + Window - 1;
        var normalizer = new AlgoEffectNormalizer();

        var blocks = new Dictionary<ulong, BlockData>();
        for (var round = first - 1; round <= last; round++)
        {
            blocks[round] = await adapter.FetchBlockAsync(round, CancellationToken.None);
        }

        var ledger = new Dictionary<ulong, IReadOnlyDictionary<Address, ulong>>();
        for (var round = first; round <= last; round++)
        {
            ledger[round] = await reference.LedgerBalancesAfterAsync(round);
        }

        var effectSums = new Dictionary<ulong, Dictionary<Address, BigInteger>>();
        var innerEffects = 0;
        for (var round = first; round <= last; round++)
        {
            var native = normalizer.NormalizeBlock(blocks[round], RewardsState.FromHeader(blocks[round - 1].Block), await LiveAssetDirectory.LoadAsync(reference, adapter, blocks[round])).Deltas
                .Where(static e => e.Token == TokenKey.Native)
                .ToArray();
            innerEffects += native.Count(static e => e.Id.InnerPath.Count > 0);
            effectSums[round] = native
                .GroupBy(static e => e.Account)
                .ToDictionary(static g => g.Key, static g => g.Aggregate(BigInteger.Zero, static (a, e) => a + e.Delta));
        }

        var lastSeen = new Dictionary<Address, (ulong Round, ulong Balance)>();
        var proposers = 0;
        var checkedPayouts = 0;
        var problems = new List<string>();
        for (var round = first; round <= last; round++)
        {
            proposers += blocks[round].Block.ProposerPayout is > 0 ? 1 : 0;
            var sums = effectSums[round];

            foreach (var (account, balance) in ledger[round])
            {
                if (lastSeen.TryGetValue(account, out var seen))
                {
                    var missing = (BigInteger)balance - seen.Balance - sums.GetValueOrDefault(account);
                    var between = Enumerable.Range(1, (int)(round - seen.Round - 1)).Select(i => seen.Round + (ulong)i)
                        .Aggregate(BigInteger.Zero, (a, r) => a + effectSums[r].GetValueOrDefault(account));
                    missing -= between;
                    if (blocks[round].Block.Proposer is { } proposer && new Address(proposer.Bytes) == account && blocks[round].Block.ProposerPayout is > 0)
                    {
                        checkedPayouts++;
                    }

                    if (!missing.IsZero)
                    {
                        problems.Add($"round {round} {ReferenceNode.Encode(account)}: ledger differs from our effects by {missing} (proposer={blocks[round].Block.Proposer?.EncodeAsString()})");
                    }
                }

                lastSeen[account] = (round, balance);
            }

            foreach (var account in sums.Keys.Where(a => !ledger[round].ContainsKey(a) && sums[a] != 0))
            {
                problems.Add($"round {round} {ReferenceNode.Encode(account)}: we changed the balance by {sums[account]} but the ledger did not");
            }
        }

        output.WriteLine($"{chain}: rounds {first}-{last}, {proposers} proposer payouts ({checkedPayouts} to proposers the ledger reported earlier in the window), {innerEffects} native effects of inner transactions, {problems.Count} problems");
        foreach (var problem in problems)
        {
            output.WriteLine(problem);
        }

        Assert.True(!expectPayouts || innerEffects > 0, "The window has no inner-transaction payments, so it proves nothing about them.");
        Assert.True(!expectPayouts || checkedPayouts > 0, "No proposer payout could be checked, so the window proves nothing about them.");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
    }
}
