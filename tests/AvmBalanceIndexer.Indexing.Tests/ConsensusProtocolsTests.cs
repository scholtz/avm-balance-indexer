using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class ConsensusProtocolsTests
{
    private const string V15 = "https://github.com/algorand/spec/tree/a26ed78ed8f834e2b9ccb6eb7d3ee9f629a6e622";
    private const string V16 = "https://github.com/algorand/spec/tree/22726c9dcd12d9cddce4a8bd7e8ccaa707f74101";
    private const string V25 = "https://github.com/algorandfoundation/specs/tree/bea19289bf41217d2c0af30522fa222ef1366466";
    private const string V26 = "https://github.com/algorandfoundation/specs/tree/ac2255d586c4474d4ebcf3809acccb59b7ef34ff";

    [Theory]
    [InlineData("v7", true, false, false)]
    [InlineData("v11", false, true, false)]
    [InlineData(V15, false, true, false)]
    [InlineData(V16, false, true, true)]
    [InlineData(V25, false, true, true)]
    [InlineData(V26, false, false, true)]
    [InlineData("unknown-future-protocol", false, false, true)]
    public void Protocol_Rules(string protocol, bool noCommitment, bool flat, bool genesisHashRequired)
    {
        Assert.Equal(noCommitment, ConsensusProtocols.HasNoPaysetCommitment(protocol));
        Assert.Equal(flat, ConsensusProtocols.UsesFlatPaysetCommitment(protocol));
        Assert.Equal(genesisHashRequired, ConsensusProtocols.RequiresGenesisHash(protocol));
    }
}
