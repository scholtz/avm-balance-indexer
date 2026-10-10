using System.Numerics;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Chains = AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Tests;

public class AlgoEffectNormalizerTests
{
    private static readonly TestChain Chain = new();
    private static readonly AlgoEffectNormalizer Normalizer = new();

    private static IReadOnlyList<BalanceDelta> Normalize(AvmBalanceIndexer.Indexing.Adapters.BlockData block, RewardsState? previous = null) =>
        Normalizer.Normalize(block, previous ?? default);

    private static readonly Chains.Address Alice = TestChain.Account(1);
    private static readonly Chains.Address Bob = TestChain.Account(2);
    private static readonly Chains.Address Carol = TestChain.Account(3);

    [Fact]
    public void Empty_Block_Has_No_Effects()
    {
        Assert.Empty(Normalize(Chain.Block(5)));
    }

    [Fact]
    public void Payment_Debits_Sender_And_Fee_Then_Credits_Receiver_And_Fee_Sink()
    {
        var deltas = Normalize(Chain.Block(7, [new TestPayment(Alice, Bob, 5_000, Fee: 1_000)]));

        Assert.Collection(
            deltas,
            d => Assert.Equal((Alice, EffectType.Fee, new BigInteger(-1_000)), (d.Account, d.Type, d.Delta)),
            d => Assert.Equal((Chain.FeeSink, EffectType.FeeCollected, new BigInteger(1_000)), (d.Account, d.Type, d.Delta)),
            d => Assert.Equal((Alice, EffectType.Payment, new BigInteger(-5_000)), (d.Account, d.Type, d.Delta)),
            d => Assert.Equal((Bob, EffectType.Payment, new BigInteger(5_000)), (d.Account, d.Type, d.Delta)));
    }

    [Fact]
    public void Effect_Ordinals_Are_Sequential_And_Unique_Within_The_Round()
    {
        var deltas = Normalize(Chain.Block(9, [new TestPayment(Alice, Bob, 1), new TestPayment(Bob, Carol, 1)]));

        Assert.Equal(Enumerable.Range(0, deltas.Count), deltas.Select(static d => d.Id.Ordinal));
        Assert.All(deltas, d => Assert.Equal(9UL, d.Id.Round));
        Assert.Equal([1, 1, 1, 1, 2, 2, 2, 2], deltas.Select(static d => d.Id.IntraRoundOrder));
    }

    [Fact]
    public void Rewards_Are_Realized_Before_The_Fee_And_Payment()
    {
        var deltas = Normalize(Chain.Block(
            10,
            [new TestPayment(Alice, Bob, 100, Fee: 1_000, SenderRewards: 40, ReceiverRewards: 7)]));

        Assert.Equal(
            [(Alice, EffectType.Reward, 40), (Bob, EffectType.Reward, 7)],
            deltas.Take(2).Select(static d => (d.Account, d.Type, (int)d.Delta)));
    }

    [Fact]
    public void Close_Remainder_Moves_The_Closing_Amount_And_Rewards_To_The_Close_Account()
    {
        var deltas = Normalize(Chain.Block(
            11,
            [new TestPayment(Alice, Bob, 100, Fee: 1_000, CloseTo: Carol, ClosingAmount: 900, CloseRewards: 3)]));

        Assert.Contains(deltas, d => d.Account == Carol && d.Type == EffectType.Reward && d.Delta == 3);
        Assert.Contains(deltas, d => d.Account == Alice && d.Type == EffectType.CloseOut && d.Delta == -900);
        Assert.Contains(deltas, d => d.Account == Carol && d.Type == EffectType.CloseOut && d.Delta == 900);
        // Everything is a transfer except the realized rewards, which are newly credited.
        Assert.Equal(new BigInteger(3), deltas.Aggregate(BigInteger.Zero, static (sum, d) => sum + d.Delta));
    }

    [Fact]
    public void Zero_Amounts_And_Zero_Fees_Produce_No_Effects()
    {
        Assert.Empty(Normalize(Chain.Block(12, [new TestPayment(Alice, Bob, 0, Fee: 0)])));
    }

    [Fact]
    public void Inner_Payments_Follow_Their_Parent_With_Their_Own_Path()
    {
        var inner = new TestPayment(Bob, Carol, 25, Fee: 0);
        var deltas = Normalize(Chain.Block(13, [new TestPayment(Alice, Bob, 100, Fee: 2_000, Inner: [inner, inner])]));

        var innerDeltas = deltas.Where(static d => d.Id.InnerPath.Count > 0).ToArray();
        Assert.Equal(4, innerDeltas.Length);
        Assert.Equal([[0], [0], [1], [1]], innerDeltas.Select(static d => d.Id.InnerPath.ToArray()));
        Assert.All(innerDeltas, d => Assert.Equal(deltas[0].Id.TransactionId, d.Id.TransactionId));
        Assert.True(deltas.Max(static d => d.Id.Ordinal) == deltas.Count - 1);
    }

    [Fact]
    public void Proposer_Payout_Moves_Funds_From_The_Fee_Sink_After_All_Transactions()
    {
        var deltas = Normalize(Chain.Block(14, [new TestPayment(Alice, Bob, 1)], proposerPayout: 500));

        var payout = deltas.Where(static d => d.Type == EffectType.ProposerPayout).ToArray();
        Assert.Equal(2, payout.Length);
        Assert.Equal((Chain.FeeSink, new BigInteger(-500)), (payout[0].Account, payout[0].Delta));
        Assert.Equal((Chain.Proposer, new BigInteger(500)), (payout[1].Account, payout[1].Delta));
        Assert.Equal(string.Empty, payout[0].Id.TransactionId);
        Assert.Equal(2, payout[0].Id.IntraRoundOrder);
    }

    [Fact]
    public void Normalization_Is_Deterministic()
    {
        var block = Chain.Block(15, [new TestPayment(Alice, Bob, 77, SenderRewards: 5)]);

        var first = Normalize(block);
        var second = Normalize(block);

        Assert.Equal(first.Select(static d => d.Id), second.Select(static d => d.Id));
        Assert.Equal(first.Select(static d => d.Delta), second.Select(static d => d.Delta));
    }

    [Fact]
    public void Transaction_Id_Is_Base32_And_Stable()
    {
        var deltas = Normalize(Chain.Block(16, [new TestPayment(Alice, Bob, 77)]));

        var id = deltas[0].Id.TransactionId;
        Assert.Equal(52, id.Length);
        Assert.Matches("^[A-Z2-7]+$", id);
        Assert.Equal(id, Normalize(Chain.Block(16, [new TestPayment(Alice, Bob, 77)]))[0].Id.TransactionId);
        Assert.NotEqual(id, Normalize(Chain.Block(16, [new TestPayment(Alice, Bob, 78)]))[0].Id.TransactionId);
    }
}
