using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.TestSupport;
using Chains = AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Tests;

public class BlockDecoderTests
{
    private static readonly TestChain Chain = new();
    private static readonly Chains.Address Alice = TestChain.Account(1);
    private static readonly Chains.Address Bob = TestChain.Account(2);

    [Fact]
    public void Decodes_Header_Hash_And_Apply_Data()
    {
        var closeTo = TestChain.Account(3);
        var block = Chain.Block(
            42,
            [new TestPayment(Alice, Bob, 10, SenderRewards: 5, ReceiverRewards: 6, CloseRewards: 7, CloseTo: closeTo, ClosingAmount: 8)]);

        Assert.Equal(42UL, block.Round);
        Assert.Equal(Chain.HashOf(42), block.Hash);
        Assert.Equal(TestChain.FakeHash(41), block.PreviousHash);
        Assert.Equal(Chain.GenesisHash, block.GenesisHash);

        var apply = Assert.Single(block.ApplyData);
        Assert.Equal((5UL, 6UL, 7UL, 8UL), (apply.SenderRewards, apply.ReceiverRewards, apply.CloseRewards, apply.ClosingAmount));
        Assert.True(apply.HasGenesisHash);
        Assert.True(apply.HasGenesisId);
        Assert.Single(block.ApplyData);
        Assert.Empty(block.Block.Transactions ?? []);
    }

    [Fact]
    public void Inner_Transactions_Keep_Their_Own_Apply_Data()
    {
        var inner = new TestPayment(Bob, Alice, 1, Fee: 0, ClosingAmount: 0);
        var block = Chain.Block(3, [new TestPayment(Alice, Bob, 2, Inner: [inner, inner])]);

        Assert.Equal(2, block.ApplyData[0].InnerTxns.Count);
    }

    [Fact]
    public void Wrong_Round_Is_Rejected()
    {
        var bytes = Chain.Encode(10, []);

        var error = Assert.Throws<BlockValidationException>(() => BlockDecoder.Decode(bytes, 11, Chain.Profile, Chain.GenesisHash));

        Assert.Contains("Requested round 11", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_Genesis_Hash_Is_Rejected()
    {
        var bytes = Chain.Encode(10, []);
        var other = new byte[32];

        Assert.Throws<BlockValidationException>(() => BlockDecoder.Decode(bytes, 10, Chain.Profile, other));
    }

    [Fact]
    public void Block_Of_Another_Chain_Is_Rejected()
    {
        var bytes = new TestChain(Chains.ChainId.AlgorandMainnet).Encode(10, []);

        Assert.Throws<BlockValidationException>(() => BlockDecoder.Decode(bytes, 10, Chain.Profile, Chain.GenesisHash));
    }

    [Fact]
    public void A_Certificate_Digest_That_Is_Not_The_Header_Hash_Is_Rejected()
    {
        var bytes = Chain.Encode(10, [new TestPayment(Alice, Bob, 1)]);
        var digest = Chain.HashOf(10);
        var at = bytes.AsSpan().IndexOf(digest);
        Assert.True(at > 0);
        bytes[at] ^= 0xFF;

        var error = Assert.Throws<BlockValidationException>(() => BlockDecoder.Decode(bytes, 10, Chain.Profile, Chain.GenesisHash));

        Assert.Contains("hash", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Tampered_Header_Field_Is_Rejected_Because_The_Hash_No_Longer_Matches()
    {
        var bytes = Chain.Encode(10, []);
        var timestampKey = new byte[] { 0xA2, (byte)'t', (byte)'s' };
        var at = bytes.AsSpan().IndexOf(timestampKey);
        Assert.True(at > 0);
        bytes[at + 6] ^= 0x01; // flips a bit inside the 32-bit timestamp value that follows the key and its type marker

        Assert.Throws<BlockValidationException>(() => BlockDecoder.Decode(bytes, 10, Chain.Profile, Chain.GenesisHash));
    }

    [Fact]
    public void The_Header_Hash_Covers_Every_Header_Field_And_Commits_To_The_Payset_Through_Txn()
    {
        var chain = new TestChain();
        var empty = BlockHashCalculator.Compute(chain.Encode(7, []));
        var withTransactions = BlockHashCalculator.Compute(chain.Encode(7, [new TestPayment(Alice, Bob, 1)]));
        var otherRound = BlockHashCalculator.Compute(chain.Encode(8, []));

        Assert.Equal(32, empty.Length);
        Assert.NotEqual(empty, withTransactions);
        Assert.NotEqual(empty, otherRound);
        Assert.Throws<InvalidDataException>(() => BlockHashCalculator.Compute(new byte[] { 0x80 }));
    }

    [Fact]
    public void Altered_Transaction_Bytes_Are_Rejected_By_The_Payset_Commitment()
    {
        var bytes = Chain.Encode(10, [new TestPayment(Alice, Bob, 1_000_000)]);
        var amountKey = new byte[] { 0xA3, (byte)'a', (byte)'m', (byte)'t' };
        var at = bytes.AsSpan().IndexOf(amountKey);
        Assert.True(at > 0);
        bytes[at + amountKey.Length + 2] ^= 0x01;

        var error = Assert.Throws<BlockValidationException>(() => BlockDecoder.Decode(bytes, 10, Chain.Profile, Chain.GenesisHash));

        Assert.Contains("commitment", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v11")]
    [InlineData("v7")]
    [InlineData("https://github.com/algorandfoundation/specs/tree/bea19289bf41217d2c0af30522fa222ef1366466")]
    public void Every_Commitment_Scheme_Round_Trips(string protocol)
    {
        var block = Chain.Block(10, [new TestPayment(Alice, Bob, 5), new TestPayment(Bob, Alice, 1)], protocol: protocol);

        Assert.Equal(2, block.ApplyData.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    public void Truncated_Or_Garbage_Payload_Is_Rejected(int length)
    {
        var bytes = Chain.Encode(10, [new TestPayment(Alice, Bob, 1)]);
        var damaged = length == 0 ? [] : bytes.Take(Math.Min(length, bytes.Length - 1)).ToArray();

        Assert.ThrowsAny<Exception>(() => BlockDecoder.Decode(damaged, 10, Chain.Profile, Chain.GenesisHash));
    }
}
