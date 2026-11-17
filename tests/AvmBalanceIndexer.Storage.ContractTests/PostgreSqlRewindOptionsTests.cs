using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Storage.ContractTests;

public class PostgreSqlRewindOptionsTests
{
    private static ValidateOptionsResult Validate(PostgreSqlRewindOptions? rewind)
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlStorage();
        using var provider = services.BuildServiceProvider();
        var options = new StorageOptions { Provider = StorageProvider.PostgreSql, PostgreSql = { ConnectionString = "Host=x", RewindTo = rewind } };
        return provider.GetServices<IValidateOptions<StorageOptions>>().Single().Validate(null, options);
    }

    [Theory]
    [InlineData("fix-2026.11_a", 0L, true)]
    [InlineData("fix-1", 123_456L, true)]
    [InlineData("", 5L, false)]
    [InlineData("has space", 5L, false)]
    [InlineData("slash/id", 5L, false)]
    [InlineData("fix-1", -1L, false)] // the round was not set
    public void The_Request_Id_And_The_Round_Are_Validated(string id, long round, bool valid) =>
        Assert.Equal(valid, !Validate(new PostgreSqlRewindOptions { RequestId = id, Round = round }).Failed);

    [Fact]
    public void An_Id_Longer_Than_64_Characters_Is_Rejected() =>
        Assert.True(Validate(new PostgreSqlRewindOptions { RequestId = new string('a', 65), Round = 1 }).Failed);

    [Fact]
    public void No_Rewind_Is_Valid() => Assert.False(Validate(null).Failed);
}
