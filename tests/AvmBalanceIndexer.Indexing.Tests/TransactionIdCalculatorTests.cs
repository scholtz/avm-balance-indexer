using System.Buffers;
using AvmBalanceIndexer.Indexing.Adapters;
using MessagePack;

namespace AvmBalanceIndexer.Indexing.Tests;

public class TransactionIdCalculatorTests
{
    private static readonly byte[] TestNetGenesisHash = Convert.FromBase64String("SGO1GKSzyE7IEPItTxCByw9x8FmnrCDexi9/cOUJOiI=");

    [Fact]
    public void Matches_The_Id_The_Public_Indexer_Reports_For_The_First_TestNet_Transaction()
    {
        // TestNet round 1: GD64YIY3... pays 3NVE2MK2... 100 ALGO with a 0.01 ALGO fee. The block stores the transaction
        // without its genesis hash and id (flags hgh and hgi), exactly as encoded here.
        var raw = WriteMap(
            ("amt", (ref w) => w.Write(100_000_000UL)),
            ("fee", (ref w) => w.Write(10_000UL)),
            ("lv", (ref w) => w.Write(1_000UL)),
            ("rcv", (ref w) => w.Write(new Algorand.Address("3NVE2MK2QYZQFOZ5XIRQTM7JRHNPUBV7QKLYLT7OO6QXFHXMRIAUXXNCBM").Bytes)),
            ("snd", (ref w) => w.Write(new Algorand.Address("GD64YIY3TWGDMCNPP553DZPPR6LDUSFQOIJVFDPPXWEG3FVOJCCDBBHU5A").Bytes)),
            ("type", (ref w) => w.Write("pay")));
        var apply = new TxnApplyData(raw, 0, 0, 0, 0, 0, HasGenesisHash: true, HasGenesisId: true, []);

        var id = TransactionIdCalculator.Compute(apply, TestNetGenesisHash, "testnet-v1.0");

        Assert.Equal("QOOBRVQMX4HW5QZ2EGLQDQCQTKRF3UP3JKDGKYPCXMI6AVV35KQA", id);
    }

    [Fact]
    public void The_Id_Depends_On_The_Genesis_When_It_Was_Stripped_From_The_Block()
    {
        var raw = WriteMap(("fee", (ref w) => w.Write(1UL)), ("type", (ref w) => w.Write("pay")));
        var stripped = new TxnApplyData(raw, 0, 0, 0, 0, 0, true, true, []);
        var kept = new TxnApplyData(raw, 0, 0, 0, 0, 0, false, false, []);

        Assert.NotEqual(
            TransactionIdCalculator.Compute(stripped, TestNetGenesisHash, "testnet-v1.0"),
            TransactionIdCalculator.Compute(kept, TestNetGenesisHash, "testnet-v1.0"));
        Assert.NotEqual(
            TransactionIdCalculator.Compute(stripped, TestNetGenesisHash, "testnet-v1.0"),
            TransactionIdCalculator.Compute(stripped, new byte[32], "testnet-v1.0"));
    }

    [Fact]
    public void Key_Order_In_The_Source_Does_Not_Matter()
    {
        var ordered = WriteMap(("amt", (ref w) => w.Write(5UL)), ("fee", (ref w) => w.Write(1UL)));
        var shuffled = WriteMap(("fee", (ref w) => w.Write(1UL)), ("amt", (ref w) => w.Write(5UL)));

        Assert.Equal(
            TransactionIdCalculator.Compute(new TxnApplyData(ordered, 0, 0, 0, 0, 0, false, false, []), TestNetGenesisHash, "x"),
            TransactionIdCalculator.Compute(new TxnApplyData(shuffled, 0, 0, 0, 0, 0, false, false, []), TestNetGenesisHash, "x"));
    }

    [Fact]
    public void A_Transaction_Without_A_Body_Is_Rejected()
    {
        var apply = new TxnApplyData(default, 0, 0, 0, 0, 0, false, false, []);

        Assert.Throws<InvalidDataException>(() => TransactionIdCalculator.Compute(apply, TestNetGenesisHash, "x"));
    }

    [Fact]
    public void Base32_Encodes_Like_Rfc4648_Without_Padding()
    {
        Assert.Equal(string.Empty, Base32.Encode([]));
        Assert.Equal("MY", Base32.Encode("f"u8));
        Assert.Equal("MZXQ", Base32.Encode("fo"u8));
        Assert.Equal("MZXW6", Base32.Encode("foo"u8));
        Assert.Equal("MZXW6YQ", Base32.Encode("foob"u8));
        Assert.Equal("MZXW6YTB", Base32.Encode("fooba"u8));
        Assert.Equal("MZXW6YTBOI", Base32.Encode("foobar"u8));
    }

    private static byte[] WriteMap(params (string Key, WriteValueAction Write)[] entries)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(entries.Length);
        foreach (var (key, write) in entries)
        {
            writer.Write(key);
            write(ref writer);
        }

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private delegate void WriteValueAction(ref MessagePackWriter writer);
}
