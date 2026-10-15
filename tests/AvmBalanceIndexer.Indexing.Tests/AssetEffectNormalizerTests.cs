using System.Numerics;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Chains = AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Tests;

public class AssetEffectNormalizerTests
{
    private const ulong Asset = 777;

    private static readonly TestChain Chain = new();
    private static readonly AlgoEffectNormalizer Normalizer = new();
    private static readonly Chains.Address Alice = TestChain.Account(1);
    private static readonly Chains.Address Bob = TestChain.Account(2);
    private static readonly Chains.Address Carol = TestChain.Account(3);
    private static readonly TokenKey Token = TokenKey.Asa(Asset);

    private static NormalizedBlock Normalize(BlockData block, IAssetDirectory? assets = null) =>
        Normalizer.NormalizeBlock(block, default, assets ?? AssetDirectory.Empty);

    private static List<(Chains.Address Account, EffectType Type, long Delta)> AssetEffects(NormalizedBlock block) =>
        [.. block.Deltas.Where(static d => d.Token == Token).Select(static d => (d.Account, d.Type, (long)d.Delta))];

    [Fact]
    public void A_Transfer_Moves_Units_And_Charges_The_Fee_In_The_Native_Token()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Bob, 0, Asset: TestAsset.Transfer(Asset, 25))]));

        Assert.Equal([(Alice, EffectType.AssetTransfer, -25L), (Bob, EffectType.AssetTransfer, 25L)], AssetEffects(block));
        Assert.Contains(block.Deltas, d => d.Token == TokenKey.Native && d.Type == EffectType.Fee && d.Account == Alice);
        Assert.Empty(block.TokenChanges);
    }

    [Fact]
    public void An_Empty_Transfer_To_Oneself_Is_An_Opt_In_That_Changes_No_Balance()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.OptIn(Asset))]));

        Assert.Equal([(Alice, EffectType.AssetOptIn, 0L)], AssetEffects(block));
    }

    [Fact]
    public void An_Empty_Transfer_To_Someone_Else_Does_Nothing()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Bob, 0, Asset: TestAsset.Transfer(Asset, 0))]));

        Assert.Empty(AssetEffects(block));
    }

    [Fact]
    public void A_Clawback_Moves_Units_From_The_Named_Account_And_The_Sender_Only_Pays_The_Fee()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Carol, 0, Asset: TestAsset.Clawback(Asset, 9, from: Bob))]));

        Assert.Equal([(Bob, EffectType.AssetClawback, -9L), (Carol, EffectType.AssetClawback, 9L)], AssetEffects(block));
    }

    [Fact]
    public void Closing_Out_Sends_The_Remainder_And_Removes_The_Holding()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Bob, 0, Asset: TestAsset.CloseOut(Asset, Carol, closingAmount: 60, amount: 15))]));

        Assert.Equal(
            [(Alice, EffectType.AssetTransfer, -15L), (Bob, EffectType.AssetTransfer, 15L), (Alice, EffectType.AssetOptOut, -60L), (Carol, EffectType.AssetTransfer, 60L)],
            AssetEffects(block));
    }

    private const string ProtocolV20 = "https://github.com/algorandfoundation/specs/tree/4a9db6a25595c6fd097cf9cc137cc83027787eaa";
    private const string ProtocolV25 = "https://github.com/algorandfoundation/specs/tree/bea19289bf41217d2c0af30522fa222ef1366466";

    [Fact]
    public void Closing_Out_An_Empty_Holding_Still_Records_The_Opt_Out()
    {
        // From v25 an absent aca is a real zero, so the delta stays exact (the default test protocol is a modern one).
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.CloseOut(Asset, Bob, closingAmount: 0))]));

        Assert.Equal([(Alice, EffectType.AssetOptOut, 0L)], AssetEffects(block));
        Assert.All(block.Deltas, static d => Assert.Equal(CloseOutRole.None, d.CloseOut));
    }

    [Fact]
    public void Closing_Out_Without_A_Stated_Amount_Leaves_The_Amount_To_The_Balance_Tracker_Before_V25()
    {
        // Protocols before v25 do not record the closed amount (no aca in the apply data; TestNet round 4848388, protocol v20): the
        // closer's whole remaining balance moves, which only the running balance knows.
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Bob, 0, Asset: TestAsset.CloseOut(Asset, Carol, closingAmount: 0, amount: 15))], protocol: ProtocolV20));

        Assert.Equal(
            [
                (Alice, EffectType.AssetTransfer, -15L, CloseOutRole.None),
                (Bob, EffectType.AssetTransfer, 15L, CloseOutRole.None),
                (Alice, EffectType.AssetOptOut, 0L, CloseOutRole.Debit),
                (Carol, EffectType.AssetTransfer, 0L, CloseOutRole.Credit),
            ],
            [.. block.Deltas.Where(static d => d.Token == Token).Select(static d => (d.Account, d.Type, (long)d.Delta, d.CloseOut))]);
        Assert.Equal(Carol, block.Deltas.Single(static d => d.CloseOut == CloseOutRole.Debit).Counterparty);
        Assert.Equal(Alice, block.Deltas.Single(static d => d.CloseOut == CloseOutRole.Credit).Counterparty);
    }

    [Fact]
    public void A_Stated_Closing_Amount_Stays_Exact_In_Every_Protocol()
    {
        foreach (var protocol in new[] { ProtocolV20, ProtocolV25 })
        {
            var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.CloseOut(Asset, Bob, closingAmount: 60))], protocol: protocol));

            Assert.Equal([(Alice, EffectType.AssetOptOut, -60L), (Bob, EffectType.AssetTransfer, 60L)], AssetEffects(block));
        }
    }

    [Fact]
    public void Creating_An_Asset_Credits_The_Creator_The_Whole_Supply_And_Records_The_Token()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(Asset, 1_000_000, decimals: 6, unit: "UNIT", name: "Unit coin"))]));

        Assert.Equal([(Alice, EffectType.AssetCreate, 1_000_000L)], AssetEffects(block));
        var token = Assert.Single(block.TokenChanges);
        Assert.Equal((Token, Alice, 6, "UNIT", "Unit coin", TokenStandard.Asa, 5UL, (ulong?)null), (token.Key, token.Creator, token.Decimals, token.Symbol, token.Name, token.Standard, token.CreatedRound, token.DeletedRound));
        Assert.Equal(1_000_000UL, AssetRecords.Total(token));
    }

    [Fact]
    public void The_Supply_Of_An_Asset_Above_The_Signed_64_Bit_Range_Is_Kept_Exactly()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(Asset, ulong.MaxValue))]));

        Assert.Equal(new BigInteger(ulong.MaxValue), block.Deltas.Single(static d => d.Token == Token).Delta);
        Assert.Equal(ulong.MaxValue, AssetRecords.Total(Assert.Single(block.TokenChanges)));
    }

    [Fact]
    public void Destroying_An_Asset_Removes_The_Whole_Supply_From_The_Creator_And_Marks_The_Token_Deleted()
    {
        var created = Normalize(Chain.Block(4, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(Asset, 500))])).TokenChanges.Single();

        // The manager (Bob) may destroy an asset the creator (Alice) holds in full.
        var block = Normalize(Chain.Block(5, [new TestPayment(Bob, Bob, 0, Asset: TestAsset.Destroy(Asset))]), new AssetDirectory([created]));

        Assert.Equal([(Alice, EffectType.AssetDestroy, -500L), (Alice, EffectType.AssetOptOut, 0L)], AssetEffects(block));
        var token = Assert.Single(block.TokenChanges);
        Assert.Equal(5UL, token.DeletedRound);
        Assert.Equal(created.Creator, token.Creator);
    }

    [Fact]
    public void An_Asset_Created_And_Destroyed_In_The_Same_Block_Needs_No_Stored_Record()
    {
        var block = Normalize(Chain.Block(
            5,
            [
                new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(Asset, 42)),
                new TestPayment(Alice, Alice, 0, Asset: TestAsset.Destroy(Asset)),
            ]));

        Assert.Equal(
            [(Alice, EffectType.AssetCreate, 42L), (Alice, EffectType.AssetDestroy, -42L), (Alice, EffectType.AssetOptOut, 0L)],
            AssetEffects(block));
        Assert.Equal(2, block.TokenChanges.Count);
    }

    [Fact]
    public void Destroying_An_Asset_That_Was_Never_Seen_Is_An_Error()
    {
        var block = Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Destroy(Asset))]);

        Assert.Throws<InvalidDataException>(() => Normalize(block));
    }

    [Fact]
    public void Inner_Asset_Transfers_Follow_Their_Parent_With_Their_Own_Path()
    {
        var inner = new TestPayment(Bob, Carol, 0, Fee: 0, Asset: TestAsset.Transfer(Asset, 3));
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Bob, 1, Inner: [inner])]));

        var effects = block.Deltas.Where(static d => d.Token == Token).ToList();
        Assert.Equal(2, effects.Count);
        Assert.All(effects, e => Assert.Equal([0], e.Id.InnerPath));
    }

    [Fact]
    public void The_Destroyed_Assets_Of_A_Block_Are_Found_Including_Inner_Transactions()
    {
        var inner = new TestPayment(Bob, Bob, 0, Fee: 0, Asset: TestAsset.Destroy(900));
        var block = Chain.Block(
            5,
            [
                new TestPayment(Alice, Alice, 0, Asset: TestAsset.Destroy(Asset)),
                new TestPayment(Alice, Bob, 1, Inner: [inner]),
                new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(901, 1)),
            ]);

        Assert.Equal([Asset, 900UL], AssetScanner.DestroyedAssets(block).Order());
    }

    [Fact]
    public void An_Asset_Creation_Without_An_Id_In_The_Apply_Data_Is_Rejected()
    {
        var block = Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(0, 1))]);

        Assert.Throws<InvalidDataException>(() => Normalize(block));
    }

    [Fact]
    public void A_Creation_Without_Parameters_In_The_Transaction_Is_Still_A_Creation()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: new TestAsset("acfg", Create: true, CreatedAssetId: Asset))]));

        Assert.Equal([(Alice, EffectType.AssetCreate, 0L)], AssetEffects(block));
        Assert.Equal(0UL, AssetRecords.Total(Assert.Single(block.TokenChanges)));
    }

    [Fact]
    public void Names_Are_Stripped_Of_Nul_Characters_Which_The_Database_Rejects()
    {
        var block = Normalize(Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(Asset, 1, unit: "AB\0\0", name: "Na\0me"))]));

        var token = Assert.Single(block.TokenChanges);
        Assert.Equal(("AB", "Name"), (token.Symbol, token.Name));
        Assert.DoesNotContain('\0', token.MetadataJson!);
    }

    [Fact]
    public void Blocks_Of_The_Early_Protocols_Identify_A_Created_Asset_By_The_Transaction_Counter()
    {
        // Counter 1,000 before the block, three transactions: the creation is the second one, so it gets 1,000 + 2.
        var chain = new TestChain { TxnCounterBefore = 1_000 };
        var block = Normalizer.NormalizeBlock(
            chain.Block(
                5,
                [
                    new TestPayment(Alice, Bob, 1),
                    new TestPayment(Alice, Alice, 0, Asset: TestAsset.Creation(0, 10)),
                    new TestPayment(Alice, Bob, 1),
                ]),
            default,
            AssetDirectory.Empty);

        Assert.Equal(TokenKey.Asa(1_002), Assert.Single(block.TokenChanges).Key);
        Assert.Single(block.Deltas, static d => d.Token == TokenKey.Asa(1_002));
    }
}

