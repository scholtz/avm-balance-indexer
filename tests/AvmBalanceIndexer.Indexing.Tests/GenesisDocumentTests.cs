using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class GenesisDocumentTests
{
    // Well-known public accounts: the Algorand rewards pool and the TestNet fee sink.
    private const string RewardsPool = "7777777777777777777777777777777777777777777777777774MSJUVU";
    private const string FeeSink = "A7NMWS3NT3IUDMLVO26ULGXGIIOUQ3ND2TXSER6EBGRZNOBOUIQXHIBGDE";

    [Fact]
    public void Parses_Allocations_With_Uint64_Safe_Balances()
    {
        var json = $$$"""
            {"alloc":[
              {"addr":"{{{RewardsPool}}}","comment":"RewardsPool","state":{"algo":125000000000000,"onl":2}},
              {"addr":"{{{FeeSink}}}","comment":"FeeSink","state":{"algo":100000,"onl":2}}
            ],"id":"v1.0","network":"testnet","proto":"x"}
            """;

        var genesis = GenesisDocument.Parse(json);

        Assert.Equal("testnet-v1.0", genesis.Id);
        Assert.Equal("v1.0", genesis.SchemaId);
        Assert.Equal(2, genesis.Accounts.Count);
        Assert.Equal(125_000_000_000_000UL, genesis.Accounts[0].Balance);
        Assert.Equal("FeeSink", genesis.Accounts[1].Comment);
        Assert.Equal(32, genesis.Accounts[0].Address.Bytes.Length);
    }

    [Fact]
    public void Rejects_Duplicate_Accounts()
    {
        var json = $$$"""
            {"alloc":[
              {"addr":"{{{FeeSink}}}","state":{"algo":1}},
              {"addr":"{{{FeeSink}}}","state":{"algo":2}}
            ],"id":"x"}
            """;

        Assert.Throws<InvalidDataException>(() => GenesisDocument.Parse(json));
    }

    [Fact]
    public void An_Allocation_Without_A_Balance_Funds_The_Account_With_Nothing()
    {
        var json = $$$"""
            {"alloc":[{"addr":"{{{FeeSink}}}","state":{"onl":2}},{"addr":"{{{RewardsPool}}}"}],"id":"v1.0","network":"x"}
            """;

        var genesis = GenesisDocument.Parse(json);

        Assert.All(genesis.Accounts, static a => Assert.Equal(0UL, a.Balance));
    }

    [Fact]
    public void An_Invalid_Address_Is_Reported_By_Name()
    {
        var json = """{"alloc":[{"addr":"NOT-AN-ADDRESS","state":{"algo":1}}],"id":"v1.0"}""";

        var error = Assert.Throws<InvalidDataException>(() => GenesisDocument.Parse(json));

        Assert.Contains("NOT-AN-ADDRESS", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"alloc\":[{\"addr\":\"nope\",\"state\":{\"algo\":1}}],\"id\":\"x\"}")]
    public void Rejects_Malformed_Documents(string json)
    {
        Assert.ThrowsAny<Exception>(() => GenesisDocument.Parse(json));
    }
}
