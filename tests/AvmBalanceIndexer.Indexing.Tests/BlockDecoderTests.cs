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
        Assert.Equal(TestChain.BlockHash(42), block.Hash);
        Assert.Equal(TestChain.BlockHash(41), block.PreviousHash);
        Assert.Equal(Chain.GenesisHash, block.GenesisHash);

        var apply = Assert.Single(block.ApplyData);
        Assert.Equal((5UL, 6UL, 7UL, 8UL), (apply.SenderRewards, apply.ReceiverRewards, apply.CloseRewards, apply.ClosingAmount));
        Assert.True(apply.HasGenesisHash);
        Assert.True(apply.HasGenesisId);
        Assert.Single(block.Block.Transactions!);
    }

    [Fact]
    public void Inner_Transactions_Keep_Their_Own_Apply_Data()
    {
        var inner = new TestPayment(Bob, Alice, 1, Fee: 0, ClosingAmount: 0);
        var block = Chain.Block(3, [new TestPayment(Alice, Bob, 2, Inner: [inner, inner])]);

        Assert.Equal(2, block.ApplyData[0].InnerTxns.Count);
        Assert.Equal(2, block.Block.Transactions!.First().Detail!.InnerTxns!.Count);
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
