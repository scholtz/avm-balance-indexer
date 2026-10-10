using System.Numerics;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Tests;

public class AmountTests
{
    [Fact]
    public void Uint64Max_RoundTrips_Through_Canonical_String()
    {
        var amount = Amount.From(ulong.MaxValue);

        Assert.Equal("18446744073709551615", amount.ToString());
        Assert.Equal(amount, Amount.Parse(amount.ToString()));
    }

    [Fact]
    public void Apply_Negative_Delta_Below_Zero_Throws()
    {
        var amount = Amount.From(5);

        Assert.Throws<InvalidOperationException>(() => amount.Apply(new BigInteger(-6)));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    public void Parse_Rejects_Non_Canonical_Input(string text)
    {
        Assert.Throws<FormatException>(() => Amount.Parse(text));
    }

    [Fact]
    public void Add_Beyond_Uint64_Does_Not_Overflow()
    {
        var sum = Amount.MaxValue.Add(Amount.From(1));

        Assert.True(sum > Amount.MaxValue);
        Assert.Equal("18446744073709551616", sum.ToString());
    }
}
