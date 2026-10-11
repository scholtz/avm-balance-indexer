using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.Api.Tests;

/// <summary>The read endpoints against the in-memory reference storage filled by the real indexer from the shared scenario.</summary>
public sealed class ReadApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var indexer = new RoundIndexer(
            Adapter(),
            new AlgoEffectNormalizer(),
            storage,
            Options.Create(new IndexerOptions { FetchConcurrency = 2, BalanceCacheEntries = 1_000 }),
            NullLogger<RoundIndexer>.Instance);
        await indexer.RunAsync(LastRound, CancellationToken.None);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chain", ChainId.AlgorandTestnet);
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("Storage:PostgreSql:ConnectionString", "Host=localhost;Database=unused;Username=u;Password=p");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChainStorage>();
                services.AddSingleton<IChainStorage>(storage);
            });
        });
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static string Text(Address address) => AddressText.Format(address);

    private async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> GetAsync(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body, response);
    }

    [Fact]
    public void Addresses_Round_Trip_Through_Their_Text_Form()
    {
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY5HFKQ", AddressText.Format(new Address(new byte[32])));
        Assert.True(AddressText.TryParse(Text(Alice), out var parsed));
        Assert.Equal(Alice, parsed);
        Assert.False(AddressText.TryParse("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY5HFLQ", out _));
        Assert.False(AddressText.TryParse("nonsense", out _));
        Assert.False(AddressText.TryParse(null, out _));
    }

    [Fact]
    public async Task Current_Holdings_List_Amounts_As_Strings_And_The_Published_Round()
    {
        var (status, body, _) = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Alice)}/tokens");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((long)LastRound, body.GetProperty("indexedRound").GetInt64());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("994109", item.GetProperty("amount").GetString());
        Assert.Equal("algo", item.GetProperty("token").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Zero_Balances_Are_Hidden_Unless_Requested()
    {
        var hidden = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Carol)}/tokens");
        var shown = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Carol)}/tokens?includeZero=true");

        Assert.Empty(hidden.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("0", Assert.Single(shown.Body.GetProperty("items").EnumerateArray()).GetProperty("amount").GetString());
    }

    [Theory]
    [InlineData(0UL, 1_000_000UL)]
    [InlineData(2UL, 996_010UL)]
    [InlineData(5UL, 994_109UL)]
    public async Task Balance_At_A_Round_Is_Exact_And_Cacheable(ulong round, ulong expected)
    {
        var (status, body, response) = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Alice)}/tokens/algo/0/balance?round={round}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), body.GetProperty("amount").GetString());
        Assert.Equal((long)round, body.GetProperty("round").GetInt64());
        Assert.Contains("max-age=3600", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Balance_Without_A_Round_Is_The_Latest_And_Is_Not_Cacheable()
    {
        var (_, body, response) = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Bob)}/tokens/algo/0/balance");

        Assert.Equal("6991", body.GetProperty("amount").GetString());
        Assert.Equal((long)LastRound, body.GetProperty("round").GetInt64());
        Assert.Null(response.Headers.CacheControl);
    }

    [Fact]
    public async Task A_Round_Beyond_The_Cursor_Is_A_404_With_The_Indexed_Round()
    {
        var (status, body, _) = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Alice)}/tokens/algo/0/balance?round=6");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal((long)LastRound, body.GetProperty("indexedRound").GetInt64());
    }

    [Fact]
    public async Task Holders_Are_Paged_With_Cursors_In_Account_Order()
    {
        var collected = new List<string>();
        string? cursor = null;
        do
        {
            var url = $"/v1/algorand-testnet/tokens/algo/0/holders?round=5&limit=2" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var (status, body, _) = await GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, status);
            collected.AddRange(body.GetProperty("items").EnumerateArray().Select(static i => i.GetProperty("account").GetString()!));
            cursor = body.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(new[] { Alice, Bob, Dave, Chain.Pool, Chain.FeeSink }.Select(Text), collected);
    }

    [Fact]
    public async Task Holders_Default_To_The_Latest_Round()
    {
        var (_, body, response) = await GetAsync("/v1/algorand-testnet/tokens/algo/0/holders");

        Assert.Equal((long)LastRound, body.GetProperty("round").GetInt64());
        Assert.Null(response.Headers.CacheControl);
    }

    [Fact]
    public async Task History_Is_Newest_First_With_Signed_Deltas_And_Counterparties()
    {
        var (status, body, _) = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Dave)}/tokens/algo/0/transactions?fromRound=5&toRound=5");

        Assert.Equal(HttpStatusCode.OK, status);
        var effect = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Payment", effect.GetProperty("type").GetString());
        Assert.Equal("10", effect.GetProperty("delta").GetString());
        Assert.Equal("3910", effect.GetProperty("balanceAfter").GetString());
        Assert.Equal(Text(Bob), effect.GetProperty("counterparty").GetString());
        Assert.Equal(0, Assert.Single(effect.GetProperty("innerPath").EnumerateArray()).GetInt32());
    }

    [Fact]
    public async Task History_Pages_Through_Everything_Once()
    {
        var rounds = new List<long>();
        string? cursor = null;
        do
        {
            var url = $"/v1/algorand-testnet/accounts/{Text(Alice)}/tokens/algo/0/transactions?limit=2" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var (_, body, _) = await GetAsync(url);
            rounds.AddRange(body.GetProperty("items").EnumerateArray().Select(static i => i.GetProperty("round").GetInt64()));
            cursor = body.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(7, rounds.Count);
        Assert.Equal(rounds.OrderByDescending(static r => r), rounds);
    }

    [Fact]
    public async Task Native_Token_Metadata_Is_Served_And_Unknown_Tokens_Are_404()
    {
        var known = await GetAsync("/v1/algorand-testnet/tokens/algo/0");
        var unknown = await GetAsync("/v1/algorand-testnet/tokens/asa/31566704");

        Assert.Equal("ALGO", known.Body.GetProperty("symbol").GetString());
        Assert.Equal(6, known.Body.GetProperty("decimals").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
    }

    [Theory]
    [InlineData("/v1/algorand-testnet/accounts/not-an-address/tokens")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens?limit=0")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens?limit=1001")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens?cursor=zz")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens/nft/1/balance")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens/algo/5/balance")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens/asa/-1/balance")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens/asa/9223372036854775808/balance")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens/algo/0/balance?round=abc")]
    [InlineData("/v1/algorand-testnet/accounts/{A}/tokens/algo/0/transactions?fromRound=5&toRound=2")]
    [InlineData("/v1/algorand-testnet/tokens/algo/0/holders?cursor=zz")]
    [InlineData("/v1/algorand-testnet/tokens/algo/0/holders?limit=abc")]
    [InlineData("/v1/algorand-testnet/tokens/algo/0/holders?includeZero=maybe")]
    public async Task Invalid_Input_Is_A_400_With_A_Message(string path)
    {
        var (status, body, _) = await GetAsync(path.Replace("{A}", Text(Alice), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("error", out _) || body.TryGetProperty("title", out _));
    }

    [Fact]
    public async Task Only_The_Canonical_Spelling_Of_An_Address_Is_Accepted()
    {
        var (status, _, _) = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Alice).ToLowerInvariant()}/tokens");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public void A_Rejected_Request_Keeps_Its_Own_Status()
    {
        Assert.Equal(413, ApiExceptionHandler.Map(new BadHttpRequestException("too large", 413))!.Value.Status);
    }

    [Fact]
    public async Task A_Bad_Query_For_Another_Chain_Is_Still_A_404()
    {
        var (status, _, _) = await GetAsync($"/v1/voi/accounts/{Text(Alice)}/tokens?limit=abc&includeZero=maybe");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task A_History_Window_Ending_At_A_Published_Round_Is_Cacheable()
    {
        var closed = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Alice)}/tokens/algo/0/transactions?toRound=3");
        var open = await GetAsync($"/v1/algorand-testnet/accounts/{Text(Alice)}/tokens/algo/0/transactions");

        Assert.Contains("max-age=3600", closed.Response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Null(open.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Other_Chains_Cannot_Be_Read_Through_The_Data_Endpoints_Either()
    {
        var (status, _, _) = await GetAsync($"/v1/voi/accounts/{Text(Alice)}/tokens");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
