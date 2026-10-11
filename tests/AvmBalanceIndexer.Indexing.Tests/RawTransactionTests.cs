using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Indexing.Tests;

public class RawTransactionTests
{
    [Fact]
    public void A_Logic_Signed_Payment_With_A_Very_Large_Program_Is_Read_From_The_Raw_Map()
    {
        // First transaction of MainNet round 64000338: a payment authorised by a 1805 byte LogicSig with a 1226 byte argument.
        // The Algorand5 SDK throws on this block, which stalled indexing for good until transactions were read directly.
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mainnet-64000338-first-txn.msgpack"));
        var apply = ApplyDataReader.ReadTransactionEntry(bytes);

        var raw = RawTransaction.Read(apply.RawTxn);

        Assert.Equal(("pay", 272_000UL, 4_000UL), (raw.Type, raw.Amount, raw.Fee));
        Assert.Equal(32, raw.Sender!.Length);
        Assert.Equal(32, raw.Receiver!.Length);
        Assert.Null(raw.CloseTo);
    }

    [Fact]
    public void A_Block_Payload_Without_Its_Payset_Keeps_Header_And_Certificate()
    {
        var chain = new TestChain();
        var payload = chain.Encode(7, [new TestPayment(TestChain.Account(1), TestChain.Account(2), 5)]);

        var stripped = BlockPayload.WithoutTransactions(payload);

        Assert.True(stripped.Length < payload.Length);
        Assert.Empty(ApplyDataReader.ReadBlockTransactions(stripped));
        Assert.Equal(BlockHashCalculator.Compute(payload), BlockHashCalculator.Compute(stripped));
        Assert.Throws<InvalidDataException>(() => BlockPayload.WithoutTransactions(new byte[] { 0x80 }));
    }
}
