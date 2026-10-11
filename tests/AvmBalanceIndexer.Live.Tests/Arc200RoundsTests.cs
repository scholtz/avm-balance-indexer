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
/// ARC-200 transfers of a window of recent Voi rounds, derived from application logs by the normalizer, against what the Voi
/// ARC indexer (Mimir) reports for the same rounds. Every transfer it reports must be among ours with the same contract,
/// parties and amount; transfers of contracts it does not know (any application that logs the event) may be extra.
/// </summary>
public class Arc200RoundsTests(ITestOutputHelper output)
{
    private const int SampleTransfers = 1000;

    private static readonly Address Zero = new(new byte[Address.Length]);

    [LiveFact]
    public async Task Voi_Recent_Arc200_Transfers_Match_The_Arc_Indexer()
    {
        using var reference = ReferenceNode.Voi();
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = ChainId.Voi, MaxAttempts = 40, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);

        // The newest transfers the ARC indexer lists give rounds that certainly contain ARC-200 traffic.
        var sampled = await reference.RecentArc200RoundsAsync(SampleTransfers);
        var normalizer = new AlgoEffectNormalizer();
        using var gate = new SemaphoreSlim(8);

        var rounds = await Task.WhenAll(sampled.Select(async round =>
        {
            await gate.WaitAsync();
            try
            {
                var previous = await adapter.FetchBlockAsync(round - 1, CancellationToken.None);
                var block = await adapter.FetchBlockAsync(round, CancellationToken.None);
                var ours = Transfers(normalizer.NormalizeBlock(block, RewardsState.FromHeader(previous.Block), AssetDirectory.Empty).Deltas);
                var theirs = (await reference.Arc200TransfersInRoundAsync(round)).Where(static t => !t.Amount.IsZero).ToList();
                return (Round: round, Ours: ours, Theirs: theirs);
            }
            finally
            {
                gate.Release();
            }
        }));

        var problems = new List<string>();
        var matched = 0;
        var extra = 0;
        var mints = 0;
        foreach (var (round, ours, theirs) in rounds)
        {
            var remaining = new List<(ulong Contract, Address From, Address To, BigInteger Amount)>(ours);
            foreach (var t in theirs)
            {
                var index = remaining.FindIndex(o => o == (t.ContractId, t.Sender, t.Receiver, t.Amount));
                if (index < 0)
                {
                    problems.Add($"round {round}: contract {t.ContractId} {ReferenceNode.Encode(t.Sender)} -> {ReferenceNode.Encode(t.Receiver)} {t.Amount} is missing from the index");
                    continue;
                }

                remaining.RemoveAt(index);
                matched++;
                mints += t.Sender == Zero ? 1 : 0;
            }

            extra += remaining.Count;
        }

        output.WriteLine($"voi rounds {sampled.Min()}-{sampled.Max()} ({sampled.Count} sampled): {matched} transfers matched ({mints} mints), {extra} more from contracts the ARC indexer does not list, {problems.Count} problems");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
        Assert.True(matched > 0, "The window has no ARC-200 transfers, so it proves nothing.");
    }

    /// <summary>Rebuilds the transfer events from the deltas: a debit followed by its credit, or a lone mint or burn.</summary>
    private static List<(ulong Contract, Address From, Address To, BigInteger Amount)> Transfers(IReadOnlyList<BalanceDelta> deltas)
    {
        var result = new List<(ulong, Address, Address, BigInteger)>();
        var arc = deltas.Where(static d => d.Token.Kind == TokenKind.Arc200).ToList();
        for (var i = 0; i < arc.Count; i++)
        {
            var d = arc[i];
            switch (d.Type)
            {
                case EffectType.Arc200Mint:
                    result.Add((d.Token.TokenId, Zero, d.Account, d.Delta));
                    break;
                case EffectType.Arc200Burn:
                    result.Add((d.Token.TokenId, d.Account, Zero, -d.Delta));
                    break;
                default:
                    if (d.Delta.Sign < 0)
                    {
                        var credit = arc[++i];
                        result.Add((d.Token.TokenId, d.Account, credit.Account, credit.Delta));
                    }

                    break;
            }
        }

        return result;
    }
}
