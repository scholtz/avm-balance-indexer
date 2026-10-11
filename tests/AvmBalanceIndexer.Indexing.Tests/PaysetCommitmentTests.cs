using System.Text;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

/// <summary>
/// Known-answer tests on real TestNet gossip blocks (saved under Fixtures): the header commitments come from the chain, not
/// from this code, so they pin the flat, Merkle and vector commitment algorithms including the padding of 1, 2, 3, 6 and 9
/// transactions and the empty payset.
/// </summary>
public class PaysetCommitmentTests
{
    private static readonly ChainProfile TestNet = ChainProfile.For(ChainId.AlgorandTestnet);

    public static TheoryData<ulong, int, string> Blocks => new()
    {
        { 2_000_000, 28, "flat" },
        { 15_000_000, 4, "merkle" },
        { 18_000_000, 7, "merkle" },
        { 68156417, 1, "vector" },
        { 68156528, 2, "vector" },
        { 68156505, 3, "vector" },
        { 68156422, 6, "vector" },
        { 68156394, 9, "vector" },
        { 68159462, 0, "vector" },
    };

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_Real_Block_Reproduces_Its_Header_Commitments(ulong round, int transactions, string scheme)
    {
        var bytes = Load(round);

        var block = BlockDecoder.Decode(bytes, round, TestNet, null);

        Assert.Equal(transactions, block.ApplyData.Count);
        Assert.Equal(scheme == "vector", Contains(bytes, "txn256"));
        Assert.Equal(scheme == "vector", Contains(bytes, "txn512"));
        Assert.Equal(scheme == "flat", ConsensusProtocols.UsesFlatPaysetCommitment(block.Block.CurrentProtocol!));
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_Real_Block_With_Any_Transaction_Byte_Changed_Is_Rejected(ulong round, int transactions, string scheme)
    {
        _ = scheme;
        if (transactions == 0)
        {
            return;
        }

        var bytes = Load(round);
        var key = Encoding.ASCII.GetBytes("txns");
        var at = bytes.AsSpan().IndexOf(key);
        Assert.True(at > 0);

        // Flip a bit inside the first stored transaction.
        var damaged = (byte[])bytes.Clone();
        damaged[at + 60] ^= 0x01;

        Assert.ThrowsAny<Exception>(() => BlockDecoder.Decode(damaged, round, TestNet, null));
    }

    private static byte[] Load(ulong round) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"testnet-v1.0-{round}.bin"));

    private static bool Contains(byte[] bytes, string key) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(key)) >= 0;
}
