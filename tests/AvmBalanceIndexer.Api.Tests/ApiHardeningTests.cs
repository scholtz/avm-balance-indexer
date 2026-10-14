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

/// <summary>Rate limiting, compression, request timeouts, forwarded headers and the published API description.</summary>
public sealed class ApiHardeningTests : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private HttpClient Create(IChainStorage? storage = null, params (string Key, string Value)[] settings)
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
                services.AddSingleton(storage ?? new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet)));
            });
        });
        _factories.Add(factory);
        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static readonly Uri Status = new("/v1/algorand-testnet/status", UriKind.Relative);

    [Fact]
    public async Task Requests_Beyond_The_Limit_Get_429_With_Retry_After_And_Health_Is_Never_Limited()
    {
        using var client = Create(settings: ("Api:RateLimitPerMinute", "3"));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync(Status);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.True(response.Headers.Contains("Retry-After"));
                Assert.Contains("Too many requests", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        for (var i = 0; i < 10; i++)
        {
            using var live = await client.GetAsync(new Uri("/healthz/live", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        Assert.Equal(3, statuses.Count(static s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(static s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task The_Retry_After_Header_Is_A_Wait_Within_The_Window()
    {
        using var client = Create(settings: ("Api:RateLimitPerMinute", "1"));
        (await client.GetAsync(Status)).Dispose();

        using var limited = await client.GetAsync(Status);

        var wait = int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(wait, 1, 60);
    }

    [Fact]
    public async Task The_Api_Description_Is_Limited_Like_The_Data_Endpoints()
    {
        using var client = Create(settings: ("Api:RateLimitPerMinute", "1"));
        (await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative))).Dispose();

        using var limited = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Error_Responses_Are_Not_Sniffed_Either()
    {
        using var client = Create(new NotStartedStorage());

        using var response = await client.GetAsync(Status);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:1111:2222:3333:4444", "2001:db8:1:2::/64")]
    public void A_Client_Is_Identified_By_Its_Address_Or_Its_IPv6_Subnet(string address, string expected)
    {
        Assert.Equal(expected, Program.ClientKey(System.Net.IPAddress.Parse(address)));
        Assert.Equal("unknown", Program.ClientKey(null));
    }

    [Fact]
    public void A_Malformed_Proxy_Network_Stops_The_Start_Instead_Of_Silently_Trusting_Everyone()
    {
        Assert.ThrowsAny<Exception>(() => Create(settings: [("Api:TrustForwardedHeaders", "true"), ("Api:KnownProxyNetworks:0", "not-a-network")]));
    }

    [Fact]
    public async Task A_Limit_Of_Zero_Turns_The_Limiter_Off()
    {
        using var client = Create(settings: ("Api:RateLimitPerMinute", "0"));

        for (var i = 0; i < 20; i++)
        {
            using var response = await client.GetAsync(Status);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Responses_Are_Compressed_When_The_Client_Accepts_It_And_Never_Sniffed()
    {
        using var client = Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, Status);
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        using var response = await client.SendAsync(request);

        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task A_Request_That_Takes_Too_Long_Is_Answered_With_504()
    {
        using var client = Create(new SlowStorage(), ("Api:RequestTimeoutSeconds", "1"));

        using var response = await client.GetAsync(Status);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task The_Client_Address_Comes_From_The_Forwarding_Header_Only_When_Trusted()
    {
        using var untrusted = Create(settings: ("Api:RateLimitPerMinute", "1"));
        using var trusted = Create(settings: [("Api:RateLimitPerMinute", "1"), ("Api:TrustForwardedHeaders", "true")]);

        var untrustedStatuses = new List<HttpStatusCode>();
        var trustedStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 2; i++)
        {
            using var a = new HttpRequestMessage(HttpMethod.Get, Status);
            a.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            using var b = new HttpRequestMessage(HttpMethod.Get, Status);
            b.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            untrustedStatuses.Add((await untrusted.SendAsync(a)).StatusCode);
            trustedStatuses.Add((await trusted.SendAsync(b)).StatusCode);
        }

        // Ignoring the header, both requests come from one address and the second is limited; trusting it, they are two clients.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], untrustedStatuses);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], trustedStatuses);
    }

    [Fact]
    public async Task The_OpenApi_Description_Lists_Every_Route_With_Response_Types_And_Can_Be_Switched_Off()
    {
        using var client = Create();
        using var off = Create(settings: ("Api:PublishOpenApi", "false"));

        var document = await client.GetFromJsonAsync<JsonElement>(new Uri("/openapi/v1.json", UriKind.Relative));
        using var disabled = await off.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        var paths = document.GetProperty("paths");
        foreach (var route in new[]
        {
            "/v1/{chain}/status",
            "/v1/{chain}/accounts/{address}/tokens",
            "/v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance",
            "/v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/transactions",
            "/v1/{chain}/tokens/{kind}/{tokenId}/holders",
            "/v1/{chain}/tokens/{kind}/{tokenId}",
            "/v1/{chain}/tokens/{kind}/{tokenId}/holders/scans",
            "/v1/{chain}/scans/{id}",
            "/v1/{chain}/scans/{id}/holders",
        })
        {
            Assert.True(paths.TryGetProperty(route, out _), $"{route} is not described");
        }

        var balance = paths.GetProperty("/v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance").GetProperty("get");
        Assert.False(string.IsNullOrWhiteSpace(balance.GetProperty("summary").GetString()));
        var responses = balance.GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("404", out _));
        Assert.True(responses.TryGetProperty("429", out _));
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
    }

    [Fact]
    public async Task The_Root_Leads_To_A_Swagger_UI_That_Brings_Its_Own_Scripts()
    {
        using var client = Create();

        using var root = await client.GetAsync(new Uri("/", UriKind.Relative));
        using var page = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));
        using var script = await client.GetAsync(new Uri("/swagger/swagger-ui-bundle.js", UriKind.Relative));
        using var configuration = await client.GetAsync(new Uri("/swagger/index.js", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("swagger/index.html", root.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("swagger-ui", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        var configurationText = await configuration.Content.ReadAsStringAsync();
        Assert.Contains("\"url\":\"../openapi/v1.json\"", configurationText, StringComparison.Ordinal);
        Assert.Contains("\"tryItOutEnabled\":true", configurationText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Chain_Parameter_Defaults_To_The_Chain_The_Deployment_Serves()
    {
        using var client = Create();

        var document = await client.GetFromJsonAsync<JsonElement>(new Uri("/openapi/v1.json", UriKind.Relative));

        var parameters = document.GetProperty("paths").GetProperty("/v1/{chain}/status").GetProperty("get").GetProperty("parameters");
        var chain = parameters.EnumerateArray().Single(static p => p.GetProperty("name").GetString() == "chain");
        Assert.Equal("algorand-testnet", chain.GetProperty("schema").GetProperty("default").GetString());
    }

    [Fact]
    public async Task Without_The_Description_The_Root_Is_A_Small_Landing_Document_Not_A_404()
    {
        using var client = Create(settings: ("Api:PublishOpenApi", "false"));

        var (status, body) = (await client.GetAsync(new Uri("/", UriKind.Relative))) is var response
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>())
            : default;
        using var ui = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("algorand-testnet", body.GetProperty("chain").GetString());
        Assert.Equal("/v1/algorand-testnet/status", body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, ui.StatusCode);
    }

    private sealed class NotStartedStorage : InMemoryChainStorage
    {
        public NotStartedStorage()
            : base(new ChainContext(ChainId.AlgorandTestnet))
        {
        }

        public override Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken) =>
            Task.FromException<ChainStatus?>(new ChainNotStartedException(chain));
    }

    private sealed class SlowStorage : InMemoryChainStorage
    {
        public SlowStorage()
            : base(new ChainContext(ChainId.AlgorandTestnet))
        {
        }

        public override async Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return await base.GetAsync(chain, cancellationToken);
        }
    }
}
