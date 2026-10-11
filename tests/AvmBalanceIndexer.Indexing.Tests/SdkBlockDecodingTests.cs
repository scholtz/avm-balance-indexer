using Algorand.Algod.Model;
using Algorand.Utils;

namespace AvmBalanceIndexer.Indexing.Tests;

/// <summary>
/// Guards the Algorand5 SDK against the regression that made us read transactions ourselves (ADR 0010): MainNet round 64000338
/// holds a payment signed by a LogicSig with a very large argument, which SDK versions before 5.0.2 could not decode.
/// </summary>
public class SdkBlockDecodingTests
{
    [Fact]
    public void The_Sdk_Decodes_The_Block_With_The_Large_LogicSig_Including_Its_Transactions()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mainnet-64000338-block.msgpack"));

        var block = Encoder.DecodeFromMsgPack<CertifiedBlock>(bytes);

        Assert.Equal(64_000_338UL, block.Block!.Round);
        Assert.Equal(41, block.Block.Transactions!.Count);
    }
}
