using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Storage.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Hosting.Tests;

public class HostingExtensionsTests
{
    private const string Connection = "Host=localhost;Database=x;Username=u;Password=p";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration, bool indexing = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddChainStorage(configuration);
        if (indexing)
        {
            services.AddAlgorandIndexing(configuration);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ethereum")]
    public void A_Missing_Or_Unknown_Chain_Is_Rejected_At_Startup(string? chain)
    {
        var configuration = Config(("Chain", chain), ("Storage:Provider", "PostgreSql"));

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddChainStorage(configuration));
    }

    [Fact]
    public void A_Missing_Provider_Is_Rejected_At_Startup()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddChainStorage(Config(("Chain", ChainId.Voi))));
    }

    [Fact]
    public void An_Unsupported_Provider_Value_Is_Rejected()
    {
        Assert.ThrowsAny<Exception>(() => new ServiceCollection().AddChainStorage(Config(("Chain", ChainId.Voi), ("Storage:Provider", "Mongo"))));
    }

    [Fact]
    public void Cassandra_Fails_Fast_Because_It_Is_Not_Implemented_Yet()
    {
        var error = Assert.Throws<NotSupportedException>(
            () => new ServiceCollection().AddChainStorage(Config(("Chain", ChainId.Voi), ("Storage:Provider", "Cassandra"))));

        Assert.Contains("PostgreSql", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSql_Registers_One_Storage_For_The_Configured_Chain()
    {
        using var provider = Build(Config(("Chain", ChainId.AlgorandMainnet), ("Storage:Provider", "PostgreSql"), ("Storage:PostgreSql:ConnectionString", Connection)));

        Assert.IsType<PostgreSqlChainStorage>(provider.GetRequiredService<IChainStorage>());
        Assert.Same(provider.GetRequiredService<IChainStorage>(), provider.GetRequiredService<IChainStorage>());
        Assert.Equal(ChainId.AlgorandMainnet, provider.GetRequiredService<ChainContext>().Chain);
    }

    [Fact]
    public void PostgreSql_Without_A_Connection_String_Fails_Validation()
    {
        using var provider = Build(Config(("Chain", ChainId.Voi), ("Storage:Provider", "PostgreSql")));

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<StorageOptions>>().Value);

        Assert.Contains("ConnectionString", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2000")]
    public void PostgreSql_Partition_Count_Is_Validated(string partitions)
    {
        using var provider = Build(Config(
            ("Chain", ChainId.Voi),
            ("Storage:Provider", "PostgreSql"),
            ("Storage:PostgreSql:ConnectionString", Connection),
            ("Storage:PostgreSql:AccountHashPartitions", partitions)));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<StorageOptions>>().Value);
    }

    [Fact]
    public void Indexing_Uses_The_Root_Chain_When_No_Algorand_Chain_Is_Set()
    {
        using var provider = Build(
            Config(("Chain", ChainId.AlgorandTestnet), ("Storage:Provider", "PostgreSql"), ("Storage:PostgreSql:ConnectionString", Connection)),
            indexing: true);

        Assert.Equal(ChainId.AlgorandTestnet, provider.GetRequiredService<IOptions<AlgorandChainOptions>>().Value.Chain);
        Assert.Equal(ChainId.AlgorandTestnet, provider.GetRequiredService<IChainAdapter>().Profile.Chain);
        Assert.NotNull(provider.GetRequiredService<RoundIndexer>());
    }

    [Fact]
    public void Empty_Strings_In_Configuration_Fall_Back_To_Chain_Defaults()
    {
        using var provider = Build(
            Config(
                ("Chain", ChainId.AlgorandMainnet),
                ("Storage:Provider", "PostgreSql"),
                ("Storage:PostgreSql:ConnectionString", Connection),
                ("Algorand:AlgodUrl", string.Empty),
                ("Algorand:ExpectedGenesisHash", string.Empty)),
            indexing: true);

        Assert.NotNull(provider.GetRequiredService<IChainAdapter>());
    }

    [Fact]
    public void Voi_Requires_An_Explicit_Genesis_Hash()
    {
        var withoutHash = Config(("Chain", ChainId.Voi), ("Storage:Provider", "PostgreSql"), ("Storage:PostgreSql:ConnectionString", Connection));
        using var failing = Build(withoutHash, indexing: true);
        var error = Assert.Throws<OptionsValidationException>(() => failing.GetRequiredService<IOptions<AlgorandChainOptions>>().Value);
        Assert.Contains("ExpectedGenesisHash", error.Message, StringComparison.Ordinal);

        var withHash = Config(
            ("Chain", ChainId.Voi),
            ("Storage:Provider", "PostgreSql"),
            ("Storage:PostgreSql:ConnectionString", Connection),
            ("Algorand:ExpectedGenesisHash", Convert.ToBase64String(new byte[32])));
        using var working = Build(withHash, indexing: true);
        Assert.Equal(ChainId.Voi, working.GetRequiredService<IChainAdapter>().Profile.Chain);
    }

    [Fact]
    public void A_Malformed_Gossip_Host_Is_Rejected()
    {
        using var provider = Build(
            Config(
                ("Chain", ChainId.AlgorandTestnet),
                ("Storage:Provider", "PostgreSql"),
                ("Storage:PostgreSql:ConnectionString", Connection),
                ("Algorand:GossipHosts:0", "not a url")),
            indexing: true);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AlgorandChainOptions>>().Value);
    }

    [Fact]
    public void Out_Of_Range_Indexer_Settings_Are_Rejected()
    {
        using var provider = Build(
            Config(
                ("Chain", ChainId.AlgorandTestnet),
                ("Storage:Provider", "PostgreSql"),
                ("Storage:PostgreSql:ConnectionString", Connection),
                ("Indexer:FetchConcurrency", "0")),
            indexing: true);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<IndexerOptions>>().Value);
    }
}
