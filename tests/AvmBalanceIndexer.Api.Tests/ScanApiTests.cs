using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.Api.Tests;

/// <summary>The asynchronous holder scan endpoints; the worker side is played by direct calls to the job store.</summary>
public sealed class ScanApiTests : IAsyncLifetime
{
    private InMemoryChainStorage _storage = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var indexer = new RoundIndexer(
            Adapter(),
            new AlgoEffectNormalizer(),
            _storage,
            Options.Create(new IndexerOptions { FetchConcurrency = 2, BalanceCacheEntries = 1_000 }),
            NullLogger<RoundIndexer>.Instance);
        await indexer.RunAsync(LastRound, CancellationToken.None);
        Start(maxActive: 20);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private void Start(int maxActive)
    {
        _client?.Dispose();
        _factory?.Dispose();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chain", ChainId.AlgorandTestnet);
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("Storage:PostgreSql:ConnectionString", "Host=localhost;Database=unused;Username=u;Password=p");
            builder.UseSetting("Scans:MaxActive", maxActive.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChainStorage>();
                services.RemoveAll<IScanJobStore>();
                services.AddSingleton<IChainStorage>(_storage);
                services.AddSingleton<IScanJobStore>(_storage);
            });
        });
        _client = _factory.CreateClient();
    }

    private static readonly Uri TokenScans = new("/v1/algorand-testnet/tokens/algo/0/holders/scans", UriKind.Relative);

    private async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(Uri uri, object? body = null)
    {
        var response = body is null ? await _client.PostAsync(uri, null) : await _client.PostAsJsonAsync(uri, body);
        return (response, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var response = await _client.GetAsync(new Uri(path, UriKind.Relative));
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static Holding Row(int index) => new(TestChain.Account(300 + index), TokenKey.Native, Amount.From((ulong)index + 1), true, 2);

    [Fact]
    public async Task A_Scan_Is_Accepted_With_A_Location_And_Defaults_To_The_Latest_Round()
    {
        var (response, body) = await PostAsync(TokenScans);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/v1/algorand-testnet/scans/{id}", response.Headers.Location!.ToString());
        Assert.Equal("Pending", body.GetProperty("state").GetString());
        Assert.Equal((long)LastRound, body.GetProperty("round").GetInt64());
        Assert.False(body.GetProperty("includeZero").GetBoolean());
        Assert.False(body.GetProperty("complete").GetBoolean());

        var (status, polled) = await GetAsync($"/v1/algorand-testnet/scans/{id}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(id, polled.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_Scan_Can_Ask_For_An_Older_Round_And_Zero_Balances()
    {
        var (_, body) = await PostAsync(TokenScans, new { round = 3, includeZero = true });

        Assert.Equal(3, body.GetProperty("round").GetInt64());
        Assert.True(body.GetProperty("includeZero").GetBoolean());
    }

    [Fact]
    public async Task The_Result_Can_Be_Read_Page_By_Page_While_The_Scan_Runs_And_After_It_Completed()
    {
        var (_, created) = await PostAsync(TokenScans);
        var id = created.GetProperty("id").GetGuid();
        await _storage.ClaimNextAsync("worker", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-2), CancellationToken.None);
        await _storage.AppendAsync(id, "worker", [Row(0), Row(1), Row(2)], "next", DateTimeOffset.UtcNow, CancellationToken.None);

        var (_, running) = await GetAsync($"/v1/algorand-testnet/scans/{id}/holders?limit=2");
        Assert.Equal("Running", running.GetProperty("state").GetString());
        Assert.False(running.GetProperty("complete").GetBoolean());
        Assert.Equal(2, running.GetProperty("items").GetArrayLength());
        var cursor = running.GetProperty("nextCursor").GetString();

        var (_, rest) = await GetAsync($"/v1/algorand-testnet/scans/{id}/holders?limit=2&cursor={Uri.EscapeDataString(cursor!)}");
        Assert.Equal(1, rest.GetProperty("items").GetArrayLength());
        Assert.False(rest.GetProperty("complete").GetBoolean());

        // Caught up with a scan that is still running: the cursor to come back with is not dropped.
        var (_, caughtUp) = await GetAsync($"/v1/algorand-testnet/scans/{id}/holders?cursor={Uri.EscapeDataString(rest.GetProperty("nextCursor").GetString()!)}");
        Assert.Equal(0, caughtUp.GetProperty("items").GetArrayLength());
        Assert.Equal(rest.GetProperty("nextCursor").GetString(), caughtUp.GetProperty("nextCursor").GetString());

        await _storage.CompleteAsync(id, "worker", DateTimeOffset.UtcNow, CancellationToken.None);
        var (_, done) = await GetAsync($"/v1/algorand-testnet/scans/{id}/holders?limit=10");
        Assert.True(done.GetProperty("complete").GetBoolean());
        Assert.Equal(3, done.GetProperty("items").GetArrayLength());
        Assert.Equal(AddressText.Format(Row(0).Account), done.GetProperty("items")[0].GetProperty("account").GetString());
        var (_, job) = await GetAsync($"/v1/algorand-testnet/scans/{id}");
        Assert.True(job.GetProperty("complete").GetBoolean());
        Assert.Equal(3, job.GetProperty("holdersFound").GetInt64());
    }

    [Fact]
    public async Task Deleting_A_Scan_Cancels_It_And_Is_Harmless_When_It_Already_Finished()
    {
        var (_, created) = await PostAsync(TokenScans);
        var id = created.GetProperty("id").GetGuid();

        using var first = await _client.DeleteAsync(new Uri($"/v1/algorand-testnet/scans/{id}", UriKind.Relative));
        using var second = await _client.DeleteAsync(new Uri($"/v1/algorand-testnet/scans/{id}", UriKind.Relative));
        using var unknown = await _client.DeleteAsync(new Uri($"/v1/algorand-testnet/scans/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Cancelled", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Scans_Beyond_The_Admission_Limit_Are_Refused_Until_Others_Finish()
    {
        Start(maxActive: 1);
        var (accepted, body) = await PostAsync(TokenScans);
        var (refused, _) = await PostAsync(TokenScans);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));

        using var cancel = await _client.DeleteAsync(new Uri($"/v1/algorand-testnet/scans/{body.GetProperty("id").GetGuid()}", UriKind.Relative));
        var (again, _) = await PostAsync(TokenScans);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    [Theory]
    [InlineData("/v1/algorand-testnet/tokens/nft/1/holders/scans", HttpStatusCode.BadRequest)]
    [InlineData("/v1/algorand-testnet/tokens/algo/7/holders/scans", HttpStatusCode.BadRequest)]
    public async Task Invalid_Tokens_Are_Rejected(string path, HttpStatusCode expected)
    {
        var (response, _) = await PostAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_Token_Nobody_Has_Seen_Is_A_404_And_No_Scan_Is_Queued()
    {
        var (response, _) = await PostAsync(new Uri("/v1/algorand-testnet/tokens/asa/999999/holders/scans", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await _storage.CountActiveAsync(DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task A_Round_That_Is_Not_Indexed_Yet_Is_A_404()
    {
        var (response, body) = await PostAsync(TokenScans, new { round = 6 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((long)LastRound, body.GetProperty("indexedRound").GetInt64());
    }

    [Theory]
    [InlineData("/v1/algorand-testnet/scans/not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("/v1/algorand-testnet/scans/not-a-guid/holders", HttpStatusCode.BadRequest)]
    [InlineData("/v1/algorand-testnet/scans/00000000-0000-0000-0000-000000000001", HttpStatusCode.NotFound)]
    [InlineData("/v1/algorand-testnet/scans/00000000-0000-0000-0000-000000000001/holders", HttpStatusCode.NotFound)]
    public async Task Unknown_Or_Malformed_Scan_Ids_Are_Reported(string path, HttpStatusCode expected)
    {
        var (status, _) = await GetAsync(path);

        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=5000")]
    [InlineData("cursor=zz")]
    public async Task Invalid_Result_Paging_Is_A_400(string query)
    {
        var (_, created) = await PostAsync(TokenScans);
        var id = created.GetProperty("id").GetGuid();

        var (status, _) = await GetAsync($"/v1/algorand-testnet/scans/{id}/holders?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
