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
        return factory.CreateClient();
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
