using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AvmBalanceIndexer.Api.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly InMemoryChainStorage _storage = new(new ChainContext(ChainId.AlgorandTestnet));
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private HttpClient CreateClient(IChainStorage? storage = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chain", ChainId.AlgorandTestnet);
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("Storage:PostgreSql:ConnectionString", "Host=localhost;Database=unused;Username=u;Password=p");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChainStorage>();
                services.AddSingleton(storage ?? _storage);
            });
        });
        _factories.Add(factory);
        return factory.CreateClient();
    }

    [Fact]
    public async Task Liveness_Does_Not_Depend_On_Storage()
    {
        using var client = CreateClient(new ThrowingStorage());

        using var response = await client.GetAsync(new Uri("/healthz/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_Requires_A_Reachable_Database()
    {
        using var healthy = CreateClient();
        using var broken = CreateClient(new ThrowingStorage());

        using var ready = await healthy.GetAsync(new Uri("/healthz/ready", UriKind.Relative));
        using var notReady = await broken.GetAsync(new Uri("/healthz/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);
    }

    [Fact]
    public async Task Status_Reports_Nothing_Indexed_Before_The_Worker_Has_Started()
    {
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        Assert.Equal("NotStarted", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("indexedRound").ValueKind);
        Assert.Equal("PostgreSql", status.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Status_Reports_The_Published_Cursor_And_Lag()
    {
        await _storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), 100, IngestionState.Backfilling, DateTimeOffset.UnixEpoch),
            [],
            CancellationToken.None);
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        Assert.Equal("Backfilling", status.GetProperty("state").GetString());
        Assert.Equal(0, status.GetProperty("indexedRound").GetInt64());
        Assert.Equal(100, status.GetProperty("lag").GetInt64());
    }

    [Theory]
    [InlineData("voi")]
    [InlineData("algorand-mainnet")]
    [InlineData("unknown")]
    public async Task A_Deployment_Rejects_Requests_For_Chains_It_Does_Not_Serve(string chain)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(new Uri($"/v1/{chain}/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("algorand-testnet", body.GetProperty("served").GetString());
    }

    private sealed class ThrowingStorage : IChainStorage
    {
        public Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken) => throw new InvalidOperationException("database is down");

        public Task UpdateProgressAsync(ChainStatus status, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<AvmBalanceIndexer.Domain.Tokens.TokenInfo?> GetAsync(AvmBalanceIndexer.Domain.Tokens.TokenKey key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task UpsertAsync(AvmBalanceIndexer.Domain.Tokens.TokenInfo token, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<Page<Holding>> GetHoldingsAsync(Address account, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<IReadOnlyDictionary<HoldingKey, Holding>> GetCurrentHoldingsAsync(IReadOnlyCollection<HoldingKey> keys, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<AvmBalanceIndexer.Domain.Tokens.Amount> GetBalanceAtRoundAsync(Address account, AvmBalanceIndexer.Domain.Tokens.TokenKey token, ulong round, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<Page<Holding>> GetHoldersAtRoundAsync(AvmBalanceIndexer.Domain.Tokens.TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<Page<AvmBalanceIndexer.Domain.Effects.TokenEffect>> GetEffectsAsync(Address account, AvmBalanceIndexer.Domain.Tokens.TokenKey token, ulong? fromRound, ulong? toRound, string? cursor, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task WriteRoundAsync(ChainStatus roundStatus, IReadOnlyList<AvmBalanceIndexer.Domain.Effects.TokenEffect> effects, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task EnsureSchemaAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
