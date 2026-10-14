using System.Text.Json;
using System.Text.Json.Nodes;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AvmBalanceIndexer.Api.Tests;

/// <summary>
/// The SDKs under sdk/ are generated from sdk/openapi/avm-balance-indexer.v1.json. This test keeps that file equal to what the API
/// publishes at /openapi/v1.json. After an API change run it with AVM_UPDATE_OPENAPI=1 to rewrite the file, then regenerate the SDKs (sdk/README.md).
/// </summary>
public sealed class OpenApiSpecTests
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    [Fact]
    public async Task The_Committed_Spec_Equals_The_Published_Spec()
    {
        var published = await PublishedSpecAsync();
        var path = SpecPath();

        if (Environment.GetEnvironmentVariable("AVM_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, published + "\n");
        }

        Assert.True(File.Exists(path), $"{path} is missing; run this test with AVM_UPDATE_OPENAPI=1.");
        var committed = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n").TrimEnd();
        Assert.True(
            string.Equals(committed, published.TrimEnd(), StringComparison.Ordinal),
            "sdk/openapi/avm-balance-indexer.v1.json is out of date. Run this test with AVM_UPDATE_OPENAPI=1, regenerate the SDKs (sdk/README.md) and commit.");
    }

    [Fact]
    public async Task The_Spec_Is_OpenApi_3_0_With_Every_Endpoint_Named()
    {
        var spec = JsonNode.Parse(await PublishedSpecAsync())!;

        Assert.StartsWith("3.0.", spec["openapi"]!.GetValue<string>(), StringComparison.Ordinal);
        var operations = spec["paths"]!.AsObject()
            .SelectMany(static path => path.Value!.AsObject().Select(static method => method.Value!["operationId"]?.GetValue<string>()))
            .ToArray();
        Assert.All(operations, static id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Contains("GetChainStatus", operations);
        Assert.Contains("CreateHolderScan", operations);
        Assert.DoesNotContain("anyOf", spec.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("/", spec["servers"]![0]!["url"]!.GetValue<string>()); // relative, for the Swagger UI (ADR 0015)

        var holders = spec["paths"]!["/v1/{chain}/tokens/{kind}/{tokenId}/holders"]!["get"]!["parameters"]!.AsArray().ToDictionary(static p => p!["name"]!.GetValue<string>(), static p => p!);
        Assert.Equal(["algorand-mainnet", "algorand-testnet", "voi"], spec["components"]!["schemas"]!["ChainId"]!["enum"]!.AsArray().Select(static n => n!.GetValue<string>()));
        Assert.Equal(["algo", "asa", "arc200"], spec["components"]!["schemas"]!["TokenKind"]!["enum"]!.AsArray().Select(static n => n!.GetValue<string>()));
        // The chain parameter is typed with the shared enum of all chains; the chain the deployment serves is its example (ADR 0015, 0016).
        Assert.Equal("#/components/schemas/ChainId", holders["chain"]["schema"]!["$ref"]!.GetValue<string>());
        Assert.Equal("algorand-testnet", holders["chain"]["example"]!.GetValue<string>());
        Assert.Equal("#/components/schemas/TokenKind", holders["kind"]["schema"]!["$ref"]!.GetValue<string>());
        Assert.Equal("integer", holders["round"]["schema"]!["type"]!.GetValue<string>());
        Assert.Equal("boolean", holders["includeZero"]["schema"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", holders["limit"]["schema"]!["type"]!.GetValue<string>());
    }

    private static async Task<string> PublishedSpecAsync()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chain", ChainId.AlgorandTestnet);
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("Storage:PostgreSql:ConnectionString", "Host=localhost;Database=unused;Username=u;Password=p");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChainStorage>();
                services.AddSingleton<IChainStorage>(new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet)));
            });
        });
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        return JsonSerializer.Serialize(JsonNode.Parse(json), Pretty).ReplaceLineEndings("\n");
    }

    private static string SpecPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AvmBalanceIndexer.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "sdk", "openapi", "avm-balance-indexer.v1.json");
    }
}
