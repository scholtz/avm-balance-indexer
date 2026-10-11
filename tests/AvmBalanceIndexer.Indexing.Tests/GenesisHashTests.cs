using System.Text.Json.Nodes;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class GenesisHashTests
{
    // Hash produced by an independent implementation of the ledger encoding that was checked against the real genesis
    // files of Algorand MainNet, TestNet and Voi.
    private const string Vector = """
        {"alloc":[{"addr":"7777777777777777777777777777777777777777777777777774MSJUVU","comment":"RewardsPool","state":{"algo":125000000000000,"onl":2}},{"addr":"A7NMWS3NT3IUDMLVO26ULGXGIIOUQ3ND2TXSER6EBGRZNOBOUIQXHIBGDE","comment":"","state":{"algo":100000,"onl":2}},{"addr":"LHHQJ6UMXRGEPXBVFKT7SY26BQOIK64VVPCLVRL3RNQLX5ZMBYG6ZHZMBE","comment":"Wallet1","state":{"algo":320000000000000,"onl":1,"sel":"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=","vote":"ZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXp7fH1+f4CBgoM=","voteKD":10000,"voteLst":3000000}}],"fees":"A7NMWS3NT3IUDMLVO26ULGXGIIOUQ3ND2TXSER6EBGRZNOBOUIQXHIBGDE","id":"v1.0","network":"testnet","proto":"https://github.com/algorand/spec/tree/a26ed78ed8f834e2b9ccb6eb7d3ee9f629a6e622","rwd":"7777777777777777777777777777777777777777777777777774MSJUVU","timestamp":1560210000}
        """;

    private const string VectorHash = "vn4eqGTIfvuAhmHThiMCoqDuZNkQBRP0VHgQzdvpiFw=";

    [Fact]
    public void The_Genesis_Hash_Matches_The_Reference_Implementation()
    {
        var genesis = GenesisDocument.Parse(Vector);

        Assert.Equal(VectorHash, Convert.ToBase64String(genesis.Hash));
        Assert.Equal("testnet-v1.0", genesis.Id);
        Assert.Equal(3, genesis.Accounts.Count);
    }

    [Fact]
    public void Formatting_And_Key_Order_Do_Not_Change_The_Hash()
    {
        var node = JsonNode.Parse(Vector)!.AsObject();
        var reversed = new JsonObject();
        foreach (var property in node.Reverse())
        {
            reversed[property.Key] = property.Value!.DeepClone();
        }

        var pretty = reversed.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(VectorHash, Convert.ToBase64String(GenesisDocument.Parse(pretty).Hash));
    }

    [Fact]
    public void Any_Change_To_A_Balance_Or_Key_Changes_The_Hash()
    {
        var richer = Vector.Replace("\"algo\":100000,", "\"algo\":100001,", StringComparison.Ordinal);
        var otherKey = Vector.Replace("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh4=", StringComparison.Ordinal);

        Assert.NotEqual(VectorHash, Convert.ToBase64String(GenesisDocument.Parse(richer).Hash));
        Assert.NotEqual(VectorHash, Convert.ToBase64String(GenesisDocument.Parse(otherKey).Hash));
    }

    [Fact]
    public void Unsupported_Values_Are_Rejected_Instead_Of_Hashed_Wrongly()
    {
        var withFloat = Vector.Replace("\"timestamp\":1560210000", "\"timestamp\":1.5", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => GenesisDocument.Parse(withFloat));
    }
}
