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

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private HttpClient CreateClient(IChainStorage? storage = null, params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chain", ChainId.AlgorandTestnet);
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("Storage:PostgreSql:ConnectionString", "Host=localhost;Database=unused;Username=u;Password=p");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChainStorage>();
                services.AddSingleton(storage ?? _storage);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
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
        Assert.Equal("NotStarted", status.GetProperty("health").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("statusAgeSeconds").ValueKind);
        Assert.Equal("EffectAggregation", status.GetProperty("historicalQueries").GetProperty("mode").GetString());
    }

    [Theory]
    [InlineData(0, "Healthy", 0)]
    [InlineData(-30, "Healthy", 30)]
    [InlineData(-120, "Healthy", 120)]
    [InlineData(-121, "Stalled", 121)]
    [InlineData(45, "Healthy", 0)] // a writer clock ahead of the API's: never a negative age
    public async Task Status_Reports_Stalled_When_The_Writer_Has_Not_Updated_It_For_Too_Long(int offsetSeconds, string health, long age)
    {
        await _storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), 0, IngestionState.Following, Now.AddSeconds(offsetSeconds)),
            [],
            CancellationToken.None);
        using var client = CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        Assert.Equal(health, status.GetProperty("health").GetString());
        Assert.Equal(age, status.GetProperty("statusAgeSeconds").GetInt64());
        Assert.Equal("Following", status.GetProperty("state").GetString()); // the state is what the writer published, health is the API's view
    }

    [Fact]
    public async Task Status_Uses_The_Configured_Stall_Threshold_And_Reports_The_Historical_Query_Limits()
    {
        await _storage.WriteRoundAsync(
            new ChainStatus(ChainId.AlgorandTestnet, 0, TestChain.FakeHash(0), 0, IngestionState.Following, Now.AddSeconds(-30)),
            [],
            CancellationToken.None);
        using var client = CreateClient(null, ("Api:StalledAfterSeconds", "20"), ("Api:RequestTimeoutSeconds", "45"), ("Scans:MaxActive", "7"));

        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        Assert.Equal("Stalled", status.GetProperty("health").GetString());
        var queries = status.GetProperty("historicalQueries");
        Assert.Equal("EffectAggregation", queries.GetProperty("mode").GetString());
        Assert.Equal(45, queries.GetProperty("synchronousTimeoutSeconds").GetInt32());
        Assert.True(queries.GetProperty("asynchronousScans").GetBoolean());
        Assert.Equal(7, queries.GetProperty("maxActiveScans").GetInt32());
    }

    [Theory]
    [InlineData("9")]
    [InlineData("86401")]
    public void A_Stall_Threshold_Outside_Its_Range_Stops_The_Api(string seconds)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chain", ChainId.AlgorandTestnet);
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("Storage:PostgreSql:ConnectionString", "Host=localhost;Database=unused;Username=u;Password=p");
            builder.UseSetting("Api:StalledAfterSeconds", seconds);
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("StalledAfterSeconds", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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

    [Fact]
    public async Task A_Chain_That_Has_Not_Been_Indexed_Yet_Is_A_503_With_A_Clear_Body()
    {
        using var client = CreateClient(new ThrowingStorage(new ChainNotStartedException(ChainId.AlgorandTestnet)));

        using var response = await client.GetAsync(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("algorand-testnet", body.GetProperty("chain").GetString());
    }

    [Fact]
    public async Task An_Unexpected_Storage_Failure_Is_Still_A_500()
    {
        using var client = CreateClient(new ThrowingStorage(new InvalidOperationException("database is down")));

        using var response = await client.GetAsync(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public void Expected_Storage_Failures_Map_To_Client_Errors()
    {
        Assert.Equal(404, ApiExceptionHandler.Map(new RoundNotIndexedException(10, 7))!.Value.Status);
        Assert.Equal(400, ApiExceptionHandler.Map(new ArgumentException("Invalid cursor.", "cursor"))!.Value.Status);
        Assert.Equal(400, ApiExceptionHandler.Map(new ArgumentOutOfRangeException("tokenId", 1UL << 63, "too large"))!.Value.Status);
        Assert.Null(ApiExceptionHandler.Map(new ArgumentException("something else", "other")));
        Assert.Null(ApiExceptionHandler.Map(new InvalidOperationException("boom")));
    }

    private sealed class ThrowingStorage(Exception? failure = null) : IChainStorage
    {
        public Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken) => throw failure ?? new InvalidOperationException("database is down");

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

        public Task<bool> WriteRoundAsync(ChainStatus roundStatus, IReadOnlyList<AvmBalanceIndexer.Domain.Effects.TokenEffect> effects, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<bool> WriteRoundsAsync(IReadOnlyList<RoundWrite> rounds, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task EnsureSchemaAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
