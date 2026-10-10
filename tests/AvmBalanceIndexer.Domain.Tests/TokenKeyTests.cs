using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Tests;

public class TokenKeyTests
{
    [Fact]
    public void Native_Token_Is_Algo_Zero()
    {
        Assert.Equal(new TokenKey(TokenKind.Algo, 0), TokenKey.Native);
        Assert.Equal("algo:0", TokenKey.Native.ToString());
    }

    [Fact]
    public void Arc20_Shares_Identity_With_Underlying_Asa()
    {
        Assert.Equal(TokenKey.Asa(31566704), new TokenKey(TokenKind.Asa, 31566704));
        Assert.NotEqual(TokenKey.Asa(1), TokenKey.Arc200(1));
    }
}
