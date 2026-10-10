using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class ChainProfileTests
{
    [Theory]
    [InlineData(ChainId.AlgorandMainnet, "mainnet-v1.0", "wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8=")]
    [InlineData(ChainId.AlgorandTestnet, "testnet-v1.0", "SGO1GKSzyE7IEPItTxCByw9x8FmnrCDexi9/cOUJOiI=")]
    public void Algorand_Networks_Pin_Their_Genesis(string chain, string genesisId, string genesisHash)
    {
        var profile = ChainProfile.For(chain);

        Assert.Equal((genesisId, genesisHash), (profile.GenesisId, profile.GenesisHash));
        Assert.Equal(32, Convert.FromBase64String(profile.GenesisHash!).Length);
    }

    [Fact]
    public void Voi_Does_Not_Pin_A_Genesis_Hash_Until_It_Is_Verified()
    {
        var profile = ChainProfile.For(ChainId.Voi);

        Assert.Null(profile.GenesisHash);
        Assert.Equal("voimain-v1.0", profile.GenesisId);
        Assert.Equal("VOI", profile.NativeSymbol);
    }

    [Fact]
    public void Unknown_Chains_Are_Rejected()
    {
        Assert.Throws<ArgumentException>(() => ChainProfile.For("solana"));
    }

    [Fact]
    public void A_Configured_Genesis_Hash_Wins_Over_The_Pinned_One()
    {
        var configured = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

        var hash = AlgorandChainFactory.ResolveGenesisHash(
            ChainProfile.For(ChainId.AlgorandTestnet),
            new AlgorandChainOptions { Chain = ChainId.AlgorandTestnet, ExpectedGenesisHash = configured });

        Assert.Equal(Convert.FromBase64String(configured), hash);
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    public void A_Malformed_Genesis_Hash_Is_Rejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => AlgorandChainFactory.ResolveGenesisHash(
            ChainProfile.For(ChainId.AlgorandTestnet),
            new AlgorandChainOptions { Chain = ChainId.AlgorandTestnet, ExpectedGenesisHash = value }));
    }

    [Fact]
    public void Chain_Ids_Are_Recognized_Exactly()
    {
        Assert.All(ChainId.All, chain => Assert.True(ChainId.IsKnown(chain)));
        Assert.False(ChainId.IsKnown("VOI"));
        Assert.Throws<ArgumentException>(() => new ChainContext("voi2"));
    }
}
