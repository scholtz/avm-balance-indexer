using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Storage.Cassandra;
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
    public void Telemetry_Is_Off_Unless_An_Endpoint_Or_The_Switch_Is_Configured()
    {
        // Only the registrations are inspected: building a provider would start an exporter that tries to reach the endpoint.
        static bool Registers(IServiceCollection services) => services.Any(static d => d.ServiceType == typeof(OpenTelemetry.Metrics.MeterProvider));

        Assert.False(Registers(new ServiceCollection().AddAvmTelemetry(Config(("Chain", ChainId.Voi)), "avm-test")));
        Assert.True(Registers(new ServiceCollection().AddAvmTelemetry(Config(("Chain", ChainId.Voi), (TelemetryExtensions.EndpointVariable, "http://localhost:4317")), "avm-test", "Some.Meter")));
        Assert.True(Registers(new ServiceCollection().AddAvmTelemetry(Config(("Telemetry:Enabled", "true")), "avm-test")));
        Assert.False(Registers(new ServiceCollection().AddAvmTelemetry(Config(("Telemetry:Enabled", "false"), (TelemetryExtensions.EndpointVariable, "http://localhost:4317")), "avm-test")));
    }

    [Fact]
    public async Task Cassandra_Registers_One_Storage_For_The_Configured_Chain_Without_Connecting()
    {
        await using var provider = Build(Config(
            ("Chain", ChainId.Voi),
            ("Storage:Provider", "Cassandra"),
            ("Storage:Cassandra:ContactPoints:0", "cassandra-0.cassandra"),
            ("Storage:Cassandra:Keyspace", "avm_voi")));

        Assert.IsType<CassandraChainStorage>(provider.GetRequiredService<IChainStorage>());
        Assert.Same(provider.GetRequiredService<IChainStorage>(), provider.GetRequiredService<IWriterLeaseProvider>());
        Assert.Same(provider.GetRequiredService<IChainStorage>(), provider.GetRequiredService<IChainStorage>());
        Assert.Equal(ChainId.Voi, provider.GetRequiredService<ChainContext>().Chain);
    }

    [Fact]
    public void Cassandra_Without_Contact_Points_Fails_Validation()
    {
        using var provider = Build(Config(("Chain", ChainId.Voi), ("Storage:Provider", "Cassandra")));

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<StorageOptions>>().Value);

        Assert.Contains("ContactPoints", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Keyspace", "bad-name; DROP")]
    [InlineData("Keyspace", "1leading_digit")]
    [InlineData("LocalDatacenter", "dc'1")]
    [InlineData("ReplicationFactor", "0")]
    [InlineData("Port", "70000")]
    public void Cassandra_Settings_That_End_Up_In_Statements_Are_Validated(string key, string value)
    {
        using var provider = Build(Config(
            ("Chain", ChainId.Voi),
            ("Storage:Provider", "Cassandra"),
            ("Storage:Cassandra:ContactPoints:0", "cassandra-0"),
            ($"Storage:Cassandra:{key}", value)));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<StorageOptions>>().Value);
    }

    [Fact]
    public void PostgreSql_Registers_One_Storage_For_The_Configured_Chain()
    {
        using var provider = Build(Config(("Chain", ChainId.AlgorandMainnet), ("Storage:Provider", "PostgreSql"), ("Storage:PostgreSql:ConnectionString", Connection)));

        Assert.IsType<PostgreSqlChainStorage>(provider.GetRequiredService<IChainStorage>());
        Assert.Same(provider.GetRequiredService<IChainStorage>(), provider.GetRequiredService<IWriterLeaseProvider>());
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
    public void Voi_Uses_Its_Pinned_Genesis_Hash_Without_Extra_Configuration()
    {
        using var provider = Build(
            Config(("Chain", ChainId.Voi), ("Storage:Provider", "PostgreSql"), ("Storage:PostgreSql:ConnectionString", Connection)),
            indexing: true);

        var adapter = provider.GetRequiredService<IChainAdapter>();

        Assert.Equal(ChainId.Voi, adapter.Profile.Chain);
        Assert.Equal(Convert.FromBase64String("r20fSQI8gWe/kFZziNonSPCXLwcQmH/nxROvnnueWOk="), adapter.GenesisHash.ToArray());
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    public void A_Malformed_Configured_Genesis_Hash_Is_Rejected(string hash)
    {
        using var provider = Build(
            Config(
                ("Chain", ChainId.AlgorandTestnet),
                ("Storage:Provider", "PostgreSql"),
                ("Storage:PostgreSql:ConnectionString", Connection),
                ("Algorand:ExpectedGenesisHash", hash)),
            indexing: true);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AlgorandChainOptions>>().Value);

        Assert.Contains("ExpectedGenesisHash", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:00", "00:01:00")]
    [InlineData("00:00:10", "00:00:05")]
    public void An_Impossible_Retry_Window_Is_Rejected(string initial, string max)
    {
        using var provider = Build(
            Config(
                ("Chain", ChainId.AlgorandTestnet),
                ("Storage:Provider", "PostgreSql"),
                ("Storage:PostgreSql:ConnectionString", Connection),
                ("Indexer:RetryInitialBackoff", initial),
                ("Indexer:RetryMaxBackoff", max)),
            indexing: true);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<IndexerOptions>>().Value);
    }

    [Fact]
    public void The_Algorand_Chain_Must_Match_The_Deployment_Chain()
    {
        using var provider = Build(
            Config(
                ("Chain", ChainId.AlgorandTestnet),
                ("Storage:Provider", "PostgreSql"),
                ("Storage:PostgreSql:ConnectionString", Connection),
                ("Algorand:Chain", ChainId.Voi)),
            indexing: true);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AlgorandChainOptions>>().Value);

        Assert.Contains("one chain", error.Message, StringComparison.Ordinal);
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
    public void An_Impossible_Fetch_Retry_Window_Is_Rejected()
    {
        using var provider = Build(
            Config(
                ("Chain", ChainId.AlgorandTestnet),
                ("Storage:Provider", "PostgreSql"),
                ("Storage:PostgreSql:ConnectionString", Connection),
                ("Algorand:BaseRetryDelay", "00:00:30"),
                ("Algorand:MaxRetryDelay", "00:00:01")),
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
