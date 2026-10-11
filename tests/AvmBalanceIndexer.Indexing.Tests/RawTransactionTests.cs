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
    public void A_Block_Holding_That_Payment_Decodes_And_Normalizes_Without_The_Sdk()
    {
        var entry = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mainnet-64000338-first-txn.msgpack"));
        var chain = new TestChain();

        var block = chain.Block(5, [new TestPayment(TestChain.Account(1), TestChain.Account(2), 0, RawEntry: entry)]);
        var normalized = new Normalization.AlgoEffectNormalizer().NormalizeBlock(block, default, Normalization.AssetDirectory.Empty);

        var payment = Assert.Single(block.ApplyData);
        Assert.Equal("pay", RawTransaction.Read(payment.RawTxn).Type);
        Assert.Contains(normalized.Deltas, static d => d.Type == Domain.Effects.EffectType.Payment && d.Delta == 272_000);
        Assert.Contains(normalized.Deltas, static d => d.Type == Domain.Effects.EffectType.Fee && d.Delta == -4_000);
    }

    [Fact]
    public void A_Transaction_Without_A_Sender_Names_The_Round_And_Transaction_In_The_Error()
    {
        var chain = new TestChain();
        var broken = new byte[] { 0x82, 0xA3, (byte)'t', (byte)'x', (byte)'n', 0x81, 0xA4, (byte)'t', (byte)'y', (byte)'p', (byte)'e', 0xA3, (byte)'p', (byte)'a', (byte)'y', 0xA3, (byte)'h', (byte)'g', (byte)'i', 0xC3 };
        var block = chain.Block(5, [new TestPayment(TestChain.Account(1), TestChain.Account(2), 0, RawEntry: broken)]);

        var error = Assert.Throws<InvalidDataException>(() => new Normalization.AlgoEffectNormalizer().NormalizeBlock(block, default, Normalization.AssetDirectory.Empty));

        Assert.Contains("Round 5, transaction 0", error.Message, StringComparison.Ordinal);
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
