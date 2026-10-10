using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Indexing.Tests;

public class RewardsPoolTests
{
    private const string PreFixProtocol = "https://github.com/algorandfoundation/specs/tree/4a9db6a25595c6fd097cf9cc137cc83027787eaa";
    private const string PostFixProtocol = "https://github.com/algorandfoundation/specs/tree/85e6db1fdbdef00aa232c75199e10dc5fe9498f6";

    private static readonly TestChain Chain = new();
    private static readonly AlgoEffectNormalizer Normalizer = new();

    [Fact]
    public void The_Pool_Is_Debited_By_Rate_Plus_Previous_Residue_Minus_New_Residue()
    {
        var previous = new RewardsState(0, 1_000, 1_000);
        var current = new RewardsState(1, 500, 1_000);

        var deltas = Normalizer.Normalize(Chain.Block(2, rewards: current), previous);

        var withdrawal = Assert.Single(deltas);
        Assert.Equal((Chain.Pool, EffectType.RewardsWithdrawal, new BigInteger(-1_500)), (withdrawal.Account, withdrawal.Type, withdrawal.Delta));
        Assert.Equal(0, withdrawal.Id.IntraRoundOrder);
    }

    [Fact]
    public void Nothing_Is_Withdrawn_When_The_Rewards_State_Did_Not_Move()
    {
        var state = new RewardsState(7, 123, 1_000);

        Assert.Empty(Normalizer.Normalize(Chain.Block(9, rewards: state), state));
    }

    [Fact]
    public void A_Round_Where_Only_The_Residue_Moves_Withdraws_Nothing()
    {
        var deltas = Normalizer.Normalize(Chain.Block(4, rewards: new RewardsState(2, 1_000, 1_000)), new RewardsState(2, 0, 1_000));

        Assert.Empty(deltas);
    }

    [Theory]
    [InlineData(PreFixProtocol, 1_050)]
    [InlineData(PostFixProtocol, 2_050)]
    [InlineData(TestChain.DefaultProtocol, 2_050)]
    public void In_The_Round_The_Rate_Changes_Only_Newer_Protocols_Use_The_New_Rate(string protocol, int expected)
    {
        var previous = new RewardsState(5, 100, 1_000);
        var current = new RewardsState(6, 50, 2_000);

        var deltas = Normalizer.Normalize(Chain.Block(500_000, rewards: current, protocol: protocol), previous);

        Assert.Equal(new BigInteger(-expected), Assert.Single(deltas).Delta);
    }

    [Fact]
    public void Rewards_Pool_Effects_Come_Before_Transaction_Effects()
    {
        var deltas = Normalizer.Normalize(
            Chain.Block(2, [new TestPayment(TestChain.Account(1), TestChain.Account(2), 5)], rewards: new RewardsState(1, 500, 1_000)),
            new RewardsState(0, 1_000, 1_000));

        Assert.Equal(EffectType.RewardsWithdrawal, deltas[0].Type);
        Assert.True(deltas.Skip(1).All(static d => d.Id.IntraRoundOrder == 1));
    }

    [Fact]
    public void An_Inconsistent_Header_Is_Rejected()
    {
        var block = Chain.Block(3, rewards: new RewardsState(1, 5_000, 1_000));

        Assert.Throws<InvalidDataException>(() => Normalizer.Normalize(block, new RewardsState(0, 100, 1_000)));
    }

    [Fact]
    public void The_Testnet_Hotfix_Moves_Funds_From_The_Bank_To_The_Pool_Before_The_Withdrawal()
    {
        var bank = new Address(new Algorand.Address("GD64YIY3TWGDMCNPP553DZPPR6LDUSFQOIJVFDPPXWEG3FVOJCCDBBHU5A").Bytes);

        var deltas = Normalizer.Normalize(
            Chain.Block(1_499_995, rewards: new RewardsState(10, 5, 1_000)),
            new RewardsState(9, 1_005, 1_000));

        Assert.Equal(
            [(bank, EffectType.LedgerAdjustment, -20_000_000_000L), (Chain.Pool, EffectType.LedgerAdjustment, 20_000_000_000L), (Chain.Pool, EffectType.RewardsWithdrawal, -2_000L)],
            deltas.Select(static d => (d.Account, d.Type, (long)d.Delta)));
    }

    [Fact]
    public void The_Hotfix_Applies_Only_To_Testnet_And_Only_In_Its_Rounds()
    {
        var state = new RewardsState(1, 1, 1);

        Assert.Empty(Normalizer.Normalize(Chain.Block(1_499_996, rewards: state), state));
        Assert.Empty(Normalizer.Normalize(new TestChain(ChainId.AlgorandMainnet).Block(1_499_995, rewards: state), state));
    }

    [Fact]
    public void The_Pinned_Testnet_Genesis_Hash_Matches_The_One_In_The_Ledger_Source()
    {
        // go-algorand identifies the TestNet genesis by this base32 digest when applying its hotfixes.
        Assert.Equal("JBR3KGFEWPEE5SAQ6IWU6EEBZMHXD4CZU6WCBXWGF57XBZIJHIRA", Base32.Encode(Chain.GenesisHash));
    }

    [Fact]
    public void Withdrawal_Equals_Reward_Units_Times_The_Level_Increase_For_Any_Inputs()
    {
        // Direct simulation of the ledger arithmetic (NextRewardsState) compared with the header-only formula.
        var random = new Random(20260711);
        for (var i = 0; i < 5_000; i++)
        {
            var units = (ulong)random.NextInt64(1, 2_000_000_000_000);
            var rate = (ulong)random.NextInt64(0, 400_000_000);
            var residue = (ulong)random.NextInt64(0, (long)units);
            var level = (ulong)random.NextInt64(0, 1_000_000);

            var withResidue = rate + residue;
            var next = new RewardsState(level + (withResidue / units), withResidue % units, rate);
            var expected = new BigInteger(units) * (next.Level - level);

            var actual = RewardsState.PoolWithdrawal(new RewardsState(level, residue, rate), next, PostFixProtocol);

            Assert.Equal(expected, actual);
        }
    }
}
