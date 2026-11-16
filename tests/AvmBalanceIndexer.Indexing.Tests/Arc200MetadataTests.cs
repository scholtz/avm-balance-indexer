using System.Numerics;
using System.Text;
using AvmBalanceIndexer.Indexing.Normalization;

namespace AvmBalanceIndexer.Indexing.Tests;

public class Arc200MetadataTests
{
    private static readonly byte[] Prefix = [0x15, 0x1f, 0x7c, 0x75];

    private static byte[] Log(params byte[][] parts) => [.. Prefix, .. parts.SelectMany(static p => p)];

    private static byte[] Padded(string text, int size)
    {
        var bytes = new byte[size];
        Encoding.UTF8.GetBytes(text).CopyTo(bytes, 0);
        return bytes;
    }

    [Theory]
    [InlineData("arc200_name()byte[32]", "657d13ec")]
    [InlineData("arc200_symbol()byte[8]", "b6ae1a25")]
    [InlineData("arc200_decimals()uint8", "84ec13d5")]
    [InlineData("arc200_totalSupply()uint256", "ec996041")]
    public void Method_Selectors_Are_The_First_Four_Bytes_Of_The_Signature_Hash(string signature, string hex) =>
        Assert.Equal(hex, Convert.ToHexStringLower(Arc200Metadata.Selector(signature)));

    [Fact]
    public void A_Padded_Name_Loses_Its_Padding()
    {
        Assert.True(Arc200Metadata.TryDecodeText(Log(Padded("Voi Token", 32)), 32, out var text));
        Assert.Equal("Voi Token", text);
    }

    [Fact]
    public void Multibyte_Text_Is_Decoded_As_Utf8()
    {
        Assert.True(Arc200Metadata.TryDecodeText(Log(Padded("Ünï", 8)), 8, out var text));
        Assert.Equal("Ünï", text);
    }

    [Fact]
    public void An_All_Zero_Name_Is_Absent() =>
        Assert.False(Arc200Metadata.TryDecodeText(Log(new byte[32]), 32, out _));

    [Fact]
    public void Invalid_Utf8_Is_Absent() =>
        Assert.False(Arc200Metadata.TryDecodeText(Log([0xff, 0xfe, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00]), 8, out _));

    [Fact]
    public void A_Wrong_Prefix_Or_Length_Is_Absent()
    {
        var wrongPrefix = Log(Padded("X", 8));
        wrongPrefix[0] = 0;
        Assert.False(Arc200Metadata.TryDecodeText(wrongPrefix, 8, out _));
        Assert.False(Arc200Metadata.TryDecodeText(Log(Padded("X", 9)), 8, out _));
        Assert.False(Arc200Metadata.TryDecodeText(Log(Padded("X", 7)), 8, out _));
        Assert.False(Arc200Metadata.TryDecodeUint(Log([6, 0]), 1, out _));
        Assert.False(Arc200Metadata.TryDecodeUint([], 1, out _));
    }

    [Fact]
    public void Decimals_Are_A_Single_Byte()
    {
        Assert.True(Arc200Metadata.TryDecodeUint(Log([6]), 1, out var value));
        Assert.Equal(6, value);
    }

    [Fact]
    public void A_Total_Supply_Above_Ulong_Is_Decoded_Big_Endian()
    {
        var supply = (BigInteger.One << 200) + 12345;
        var minimal = supply.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[32];
        minimal.CopyTo(padded, 32 - minimal.Length);

        Assert.True(Arc200Metadata.TryDecodeUint(Log(padded), 32, out var value));
        Assert.Equal(supply, value);
    }

    [Fact]
    public void Only_A_Record_With_All_Four_Values_Is_Complete()
    {
        Assert.True(new Arc200Metadata("N", "S", 6, 1).Complete);
        Assert.False(new Arc200Metadata("N", "S", 6, null).Complete);
        Assert.False(new Arc200Metadata(null, null, null, null).Complete);
    }
}
