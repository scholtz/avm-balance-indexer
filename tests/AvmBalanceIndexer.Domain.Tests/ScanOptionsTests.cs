using System.ComponentModel.DataAnnotations;
using AvmBalanceIndexer.Domain.Storage;

namespace AvmBalanceIndexer.Domain.Tests;

public class ScanOptionsTests
{
    private static List<ValidationResult> Validate(ScanOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_Defaults_Are_Valid()
    {
        Assert.Empty(Validate(new ScanOptions()));
    }

    [Fact]
    public void A_Scan_Must_Not_Look_Abandoned_Between_Two_Checkpoints()
    {
        var results = Validate(new ScanOptions { StaleAfter = TimeSpan.FromSeconds(10), RetryDelay = TimeSpan.FromSeconds(8) });

        Assert.Contains(results, static r => r.ErrorMessage!.Contains("StaleAfter", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void The_Page_Size_Is_Bounded(int pageSize)
    {
        Assert.NotEmpty(Validate(new ScanOptions { PageSize = pageSize }));
    }
}
