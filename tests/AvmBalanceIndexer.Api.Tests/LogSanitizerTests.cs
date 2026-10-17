namespace AvmBalanceIndexer.Api.Tests;

public class LogSanitizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("/v1/algorand-testnet/status", "/v1/algorand-testnet/status")]
    [InlineData("/a\nb", "/a\\u000ab")]
    [InlineData("a b", "a\u2028b")]
    [InlineData("\r\n\t", "\\u000d\\u000a\\u0009")]
    public void Control_Characters_Are_Escaped_And_Other_Text_Is_Untouched(string? input, string expected) =>
        Assert.Equal(expected, LogSanitizer.Escape(input));
}
