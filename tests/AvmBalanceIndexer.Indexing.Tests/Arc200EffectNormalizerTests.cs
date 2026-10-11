using System.Numerics;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Chains = AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Tests;

public class Arc200EffectNormalizerTests
{
    private const ulong App = 4242;

    private static readonly TestChain Chain = new();
    private static readonly AlgoEffectNormalizer Normalizer = new();
    private static readonly Chains.Address Alice = TestChain.Account(1);
    private static readonly Chains.Address Bob = TestChain.Account(2);
    private static readonly Chains.Address Zero = new(new byte[Chains.Address.Length]);
    private static readonly TokenKey Token = TokenKey.Arc200(App);

    private static NormalizedBlock Normalize(BlockData block) => Normalizer.NormalizeBlock(block, default, AssetDirectory.Empty);

    private static List<(Chains.Address Account, EffectType Type, BigInteger Delta)> Effects(NormalizedBlock block) =>
        [.. block.Deltas.Where(static d => d.Token == Token).Select(static d => (d.Account, d.Type, d.Delta))];

    private static TestPayment Call(IReadOnlyList<byte[]> logs, ulong application = App, ulong created = 0, IReadOnlyList<TestPayment>? inner = null) =>
        new(Alice, Bob, 0, Inner: inner, Application: new TestApplication(application, logs, created));

    [Fact]
    public void A_Transfer_Log_Round_Trips_Through_The_Decoder()
    {
        Assert.True(Arc200Events.TryParseTransfer(TestChain.Arc200TransferLog(Alice, Bob, 7), out var from, out var to, out var amount));
        Assert.Equal(Alice.ToArray(), from);
        Assert.Equal(Bob.ToArray(), to);
        Assert.Equal(new BigInteger(7), amount);
    }

    [Fact]
    public void A_Transfer_Log_Moves_The_Amount_Between_The_Accounts()
    {
        var block = Normalize(Chain.Block(5, [Call([TestChain.Arc200TransferLog(Alice, Bob, 250)])]));

        Assert.Equal(
            [(Alice, EffectType.Arc200Transfer, new BigInteger(-250)), (Bob, EffectType.Arc200Transfer, new BigInteger(250))],
            Effects(block));
        var token = Assert.Single(block.TokenChanges);
        Assert.Equal((Token, TokenStandard.Arc200, false, 5UL), (token.Key, token.Standard, token.StandardVerified, token.CreatedRound));
    }

    [Fact]
    public void A_Transfer_From_The_Zero_Address_Is_A_Mint_And_To_The_Zero_Address_A_Burn()
    {
        var block = Normalize(Chain.Block(5, [Call([TestChain.Arc200TransferLog(Zero, Alice, 100), TestChain.Arc200TransferLog(Alice, Zero, 40)])]));

        Assert.Equal(
            [(Alice, EffectType.Arc200Mint, new BigInteger(100)), (Alice, EffectType.Arc200Burn, new BigInteger(-40))],
            Effects(block));
        Assert.Single(block.TokenChanges);
    }

    [Fact]
    public void Amounts_Above_The_Unsigned_64_Bit_Range_Are_Kept_Exactly()
    {
        var huge = (BigInteger.One << 255) + 12345;

        var block = Normalize(Chain.Block(5, [Call([TestChain.Arc200TransferLog(Zero, Alice, huge)])]));

        Assert.Equal([(Alice, EffectType.Arc200Mint, huge)], Effects(block));
    }

    [Fact]
    public void Logs_Of_Other_Shapes_And_Zero_Amounts_Are_Ignored()
    {
        var transfer = TestChain.Arc200TransferLog(Alice, Bob, 5);
        var wrongSelector = (byte[])transfer.Clone();
        wrongSelector[0] ^= 0xFF;
        var block = Normalize(Chain.Block(5, [Call([wrongSelector, transfer[..99], [.. transfer, 0], [], TestChain.Arc200TransferLog(Alice, Bob, 0), TestChain.Arc200TransferLog(Zero, Zero, 9)])]));

        Assert.Empty(Effects(block));
        Assert.Empty(block.TokenChanges);
    }

    [Fact]
    public void An_Application_Creation_Logs_Under_The_Created_Application_Id()
    {
        var block = Normalize(Chain.Block(5, [Call([TestChain.Arc200TransferLog(Zero, Alice, 1_000)], application: 0, created: 9001)]));

        var effect = Assert.Single(block.Deltas, static d => d.Token.Kind == TokenKind.Arc200);
        Assert.Equal((TokenKey.Arc200(9001), EffectType.Arc200Mint), (effect.Token, effect.Type));
    }

    [Fact]
    public void Logs_Of_Inner_Application_Calls_Count_Under_Their_Own_Application_With_Their_Path()
    {
        var inner = Call([TestChain.Arc200TransferLog(Alice, Bob, 3)], application: 777);
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Bob, 1, Inner: [inner])]));

        var effects = block.Deltas.Where(static d => d.Token.Kind == TokenKind.Arc200).ToList();
        Assert.Equal(2, effects.Count);
        Assert.All(effects, e => Assert.Equal(TokenKey.Arc200(777), e.Token));
        Assert.All(effects, e => Assert.Equal([0], e.Id.InnerPath));
    }

    [Fact]
    public void A_Token_Is_Recorded_Once_Per_Block_Even_With_Several_Transfers()
    {
        var log = TestChain.Arc200TransferLog(Alice, Bob, 1);
        var block = Normalize(Chain.Block(5, [Call([log, log]), Call([log])]));

        Assert.Equal(6, Effects(block).Count);
        Assert.Single(block.TokenChanges);
    }
}
