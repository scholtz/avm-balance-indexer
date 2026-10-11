using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Chains = AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Tests;

public class Arc20ClassificationTests
{
    private const ulong Asset = 900;
    private const ulong App = 77;

    private static readonly TestChain Chain = new();
    private static readonly AlgoEffectNormalizer Normalizer = new();
    private static readonly Chains.Address Alice = TestChain.Account(1);
    private static readonly Chains.Address Bob = TestChain.Account(2);
    private static readonly Chains.Address AppAccount = ApplicationAddresses.Of(App);

    private static NormalizedBlock Normalize(BlockData block, params TokenInfo[] known) =>
        Normalizer.NormalizeBlock(block, default, new AssetDirectory(known));

    /// <summary>An application call that issues the asset creation as an inner transaction, as the ARC-20 contract does.</summary>
    private static TestPayment CreatedByApplication(ulong application, TestAsset creation) =>
        new(Alice, Bob, 0, Application: new TestApplication(application), Inner: [new TestPayment(ApplicationAddresses.Of(application), ApplicationAddresses.Of(application), 0, Fee: 0, Asset: creation)]);

    private static TestAsset Smart(Chains.Address? clawback, bool frozen = true) =>
        TestAsset.Creation(Asset, 1_000, decimals: 2, unit: "S-ASA", name: "SMART-ASA") with { DefaultFrozen = frozen, ClawbackAddress = clawback, Manager = AppAccount };

    [Fact]
    public void An_Asset_Created_By_An_Application_That_Holds_Its_Clawback_And_Starts_Frozen_Is_An_Arc20_Smart_Asa()
    {
        var token = Assert.Single(Normalize(Chain.Block(5, [CreatedByApplication(App, Smart(AppAccount))])).TokenChanges);

        Assert.Equal((TokenStandard.Arc20, true, (ulong?)App, TokenKey.Asa(Asset)), (token.Standard, token.StandardVerified, token.ControllingAppId, token.Key));
        Assert.Equal(AppAccount, token.Creator);
        Assert.Contains(new Algorand.Address(AppAccount.ToArray()).EncodeAsString(), token.MetadataJson!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Asset_That_Does_Not_Start_Frozen_Is_Not_Arc20()
    {
        var token = Assert.Single(Normalize(Chain.Block(5, [CreatedByApplication(App, Smart(AppAccount, frozen: false))])).TokenChanges);

        Assert.Equal((TokenStandard.Asa, (ulong?)null), (token.Standard, token.ControllingAppId));
    }

    [Fact]
    public void An_Asset_Whose_Clawback_Is_Another_Account_Is_Not_Arc20()
    {
        var token = Assert.Single(Normalize(Chain.Block(5, [CreatedByApplication(App, Smart(Bob))])).TokenChanges);

        Assert.Equal(TokenStandard.Asa, token.Standard);
    }

    [Fact]
    public void An_Asset_Created_Outside_An_Application_Is_Not_Arc20_Even_With_An_Application_Clawback()
    {
        var block = Chain.Block(5, [new TestPayment(Alice, Alice, 0, Asset: Smart(AppAccount))]);

        Assert.Equal(TokenStandard.Asa, Assert.Single(Normalize(block).TokenChanges).Standard);
    }

    [Fact]
    public void An_Application_Cannot_Claim_The_Clawback_Of_Another_Application()
    {
        var token = Assert.Single(Normalize(Chain.Block(5, [CreatedByApplication(App + 1, Smart(AppAccount))])).TokenChanges);

        Assert.Equal(TokenStandard.Asa, token.Standard);
    }

    [Fact]
    public void Reconfiguring_The_Addresses_Updates_The_Record_And_Drops_The_Arc20_Classification()
    {
        var smart = Assert.Single(Normalize(Chain.Block(5, [CreatedByApplication(App, Smart(AppAccount))])).TokenChanges);

        var changed = Assert.Single(Normalize(Chain.Block(6, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Reconfiguration(Asset, Bob, null))]), smart).TokenChanges);

        Assert.Equal((TokenStandard.Asa, (ulong?)null, 1_000UL), (changed.Standard, changed.ControllingAppId, AssetRecords.Total(changed)));
        Assert.Contains(new Algorand.Address(Bob.ToArray()).EncodeAsString(), changed.MetadataJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(new Algorand.Address(AppAccount.ToArray()).EncodeAsString(), changed.MetadataJson!, StringComparison.Ordinal);
        Assert.Equal(smart.Creator, changed.Creator);
    }

    [Fact]
    public void Reconfiguring_An_Arc20_Asset_Without_Touching_The_Clawback_Keeps_The_Classification()
    {
        var smart = Assert.Single(Normalize(Chain.Block(5, [CreatedByApplication(App, Smart(AppAccount))])).TokenChanges);

        var changed = Assert.Single(Normalize(Chain.Block(6, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Reconfiguration(Asset, Bob, AppAccount))]), smart).TokenChanges);

        Assert.Equal((TokenStandard.Arc20, (ulong?)App), (changed.Standard, changed.ControllingAppId));
    }

    [Fact]
    public void Reconfiguring_An_Unknown_Asset_Changes_Nothing()
    {
        var block = Normalize(Chain.Block(6, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Reconfiguration(Asset, Bob, null))]));

        Assert.Empty(block.TokenChanges);
    }

    [Fact]
    public void Blocks_That_Reconfigure_Assets_Need_Their_Records()
    {
        var block = Chain.Block(6, [new TestPayment(Alice, Alice, 0, Asset: TestAsset.Reconfiguration(Asset, Bob, null)), new TestPayment(Alice, Alice, 0, Asset: TestAsset.Destroy(901))]);

        Assert.Equal([Asset, 901UL], AssetScanner.ReferencedAssets(block).Order());
        Assert.Equal([901UL], AssetScanner.DestroyedAssets(block));
    }
}
