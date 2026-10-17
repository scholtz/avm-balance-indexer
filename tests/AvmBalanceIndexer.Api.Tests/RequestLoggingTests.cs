using System.Net;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;

namespace AvmBalanceIndexer.Api.Tests;

/// <summary>The API writes one structured log line per request, at a level that keeps probes out of the way.</summary>
public sealed class RequestLoggingTests : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private sealed class UnreachableStorage : InMemoryChainStorage
    {
        public UnreachableStorage()
            : base(new ChainContext(ChainId.AlgorandTestnet))
        {
        }

        public override Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken) =>
            Task.FromException<ChainStatus?>(new InvalidOperationException("database down"));
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events)
            {
                Events.Add(logEvent);
            }
        }

        public List<LogEvent> RequestLines()
        {
            lock (Events)
            {
                return [.. Events.Where(e => e.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal))];
            }
        }
    }

    private (HttpClient Client, CollectingSink Sink) Create(params (string Key, string Value)[] settings) => Create(failingStorage: false, settings);

    private (HttpClient Client, CollectingSink Sink) Create(bool failingStorage, params (string Key, string Value)[] settings)
    {
        var sink = new CollectingSink();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chain", ChainId.AlgorandTestnet);
            builder.UseSetting("Storage:Provider", "PostgreSql");
            builder.UseSetting("Storage:PostgreSql:ConnectionString", "Host=localhost;Database=unused;Username=u;Password=p");
            builder.UseSetting("Serilog:MinimumLevel:Default", "Debug");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChainStorage>();
                services.AddSingleton<IChainStorage>(failingStorage ? new UnreachableStorage() : new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet)));
                services.AddSingleton<ILogEventSink>(sink);
            });
        });
        _factories.Add(factory);
        return (factory.CreateClient(), sink);
    }

    [Fact]
    public async Task A_Request_Is_Logged_Once_With_Method_Path_Status_And_Elapsed_Time()
    {
        var (client, sink) = Create();

        using var response = await client.GetAsync(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        var line = Assert.Single(sink.RequestLines());
        Assert.Equal(LogEventLevel.Information, line.Level);
        Assert.Equal("Serilog.AspNetCore.RequestLoggingMiddleware", ((ScalarValue)line.Properties["SourceContext"]).Value);
        Assert.Equal("GET", ((ScalarValue)line.Properties["RequestMethod"]).Value);
        Assert.Equal("/v1/algorand-testnet/status", ((ScalarValue)line.Properties["RequestPath"]).Value);
        Assert.Equal((int)response.StatusCode, ((ScalarValue)line.Properties["StatusCode"]).Value);
        Assert.True(line.Properties.ContainsKey("Elapsed"));
    }

    [Fact]
    public async Task Health_Probes_Are_Logged_At_Debug()
    {
        var (client, sink) = Create();

        using var response = await client.GetAsync(new Uri("/healthz/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LogEventLevel.Debug, Assert.Single(sink.RequestLines()).Level);
    }

    [Fact]
    public async Task A_Failing_Readiness_Probe_Stays_At_Debug()
    {
        var (client, sink) = Create(failingStorage: true);

        using var response = await client.GetAsync(new Uri("/healthz/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(LogEventLevel.Debug, Assert.Single(sink.RequestLines()).Level);
    }

    [Fact]
    public async Task Rate_Limited_Requests_Are_Logged_At_Debug()
    {
        var (client, sink) = Create(("Api:RateLimitPerMinute", "1"));

        using var first = await client.GetAsync(new Uri("/v1/algorand-testnet/status", UriKind.Relative));
        using var second = await client.GetAsync(new Uri("/v1/algorand-testnet/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal([LogEventLevel.Information, LogEventLevel.Debug], sink.RequestLines().Select(e => e.Level));
    }

    [Fact]
    public async Task Control_Characters_In_The_Path_Cannot_Forge_A_Log_Line()
    {
        var (client, sink) = Create();

        using var response = await client.GetAsync(new Uri("/x%0A[INF]%20forged", UriKind.Relative));

        var line = Assert.Single(sink.RequestLines());
        var rendered = line.RenderMessage();
        Assert.DoesNotContain(Environment.NewLine, rendered, StringComparison.Ordinal);
        Assert.Contains("\\u000a[INF] forged", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_Errors_Are_Logged_At_Debug()
    {
        var (client, sink) = Create();

        using var response = await client.GetAsync(new Uri("/v1/algorand-testnet/unknown", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(LogEventLevel.Debug, Assert.Single(sink.RequestLines()).Level);
    }
}
