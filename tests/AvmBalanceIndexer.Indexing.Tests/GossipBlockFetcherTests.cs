using System.Net;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvmBalanceIndexer.Indexing.Tests;

public class GossipBlockFetcherTests
{
    private const string HostA = "http://a.test:4160";
    private const string HostB = "http://b.test:4160";

    private static readonly TestChain Chain = new();

    private static GossipBlockFetcher CreateFetcher(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string[]? hosts = null,
        int maxAttempts = 4,
        TimeSpan? timeout = null,
        int maxBlockBytes = 1024 * 1024,
        TimeProvider? time = null)
    {
        var options = new AlgorandChainOptions
        {
            Chain = Chain.Profile.Chain,
            MaxAttempts = maxAttempts,
            BaseRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(5),
            MaxBlockBytes = maxBlockBytes,
            HostCooldown = TimeSpan.FromMinutes(1),
        };

        return new GossipBlockFetcher(
            new HttpClient(new StubHandler(handler)),
            new StaticGossipHostProvider(hosts ?? [HostA, HostB]),
            Chain.Profile,
            options,
            Chain.GenesisHash,
            NullLogger<GossipBlockFetcher>.Instance,
            time);
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    [Fact]
    public async Task Fetches_And_Decodes_A_Block_Using_The_Gossip_Url()
    {
        Uri? requested = null;
        var fetcher = CreateFetcher((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(Ok(Chain.Encode(1234, [])));
        });

        var block = await fetcher.FetchBlockAsync(1234, CancellationToken.None);

        Assert.Equal(1234UL, block.Round);
        Assert.EndsWith("/v1/testnet-v1.0/block/ya", requested!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fails_Over_To_Another_Host_When_One_Has_No_Block()
    {
        var served = new List<string>();
        var fetcher = CreateFetcher((request, _) =>
        {
            served.Add(request.RequestUri!.Host);
            return Task.FromResult(request.RequestUri.Host == "a.test"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Ok(Chain.Encode(5, [])));
        });

        var block = await fetcher.FetchBlockAsync(5, CancellationToken.None);

        Assert.Equal(5UL, block.Round);
        Assert.Contains("b.test", served);
    }

    [Fact]
    public async Task Retries_Server_Errors_On_Other_Hosts()
    {
        var calls = 0;
        var fetcher = CreateFetcher((_, _) =>
            Task.FromResult(Interlocked.Increment(ref calls) < 3 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Ok(Chain.Encode(8, []))));

        var block = await fetcher.FetchBlockAsync(8, CancellationToken.None);

        Assert.Equal(8UL, block.Round);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Reports_Block_Not_Available_When_Every_Host_Returns_404()
    {
        var fetcher = CreateFetcher((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)), maxAttempts: 3);

        var error = await Assert.ThrowsAsync<BlockNotAvailableException>(() => fetcher.FetchBlockAsync(99, CancellationToken.None));

        Assert.Equal(99UL, error.Round);
    }

    [Fact]
    public async Task Fails_After_Max_Attempts_With_The_Last_Error()
    {
        var calls = 0;
        var fetcher = CreateFetcher(
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            },
            maxAttempts: 3);

        var error = await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(7, CancellationToken.None));

        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Rejects_A_Response_For_The_Wrong_Round_And_Tries_Another_Host()
    {
        var fetcher = CreateFetcher((request, _) =>
            Task.FromResult(Ok(request.RequestUri!.Host == "a.test" ? Chain.Encode(2, []) : Chain.Encode(3, []))));

        var block = await fetcher.FetchBlockAsync(3, CancellationToken.None);

        Assert.Equal(3UL, block.Round);
    }

    [Fact]
    public async Task Rejects_Oversized_Responses()
    {
        var fetcher = CreateFetcher(
            (_, _) => Task.FromResult(Ok(new byte[4096])),
            maxAttempts: 2,
            maxBlockBytes: 1024);

        await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task A_Slow_Host_Times_Out_And_Another_Host_Is_Used()
    {
        var fetcher = CreateFetcher(
            async (request, cancellationToken) =>
            {
                if (request.RequestUri!.Host == "a.test")
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }

                return Ok(Chain.Encode(11, []));
            },
            timeout: TimeSpan.FromMilliseconds(100));

        var block = await fetcher.FetchBlockAsync(11, CancellationToken.None);

        Assert.Equal(11UL, block.Round);
    }

    [Fact]
    public async Task A_Failing_Host_Is_Skipped_While_It_Cools_Down()
    {
        var hostA = 0;
        var fetcher = CreateFetcher((request, _) =>
        {
            if (request.RequestUri!.Host == "a.test")
            {
                Interlocked.Increment(ref hostA);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(Ok(Chain.Encode(21, [])));
        });

        for (var i = 0; i < 10; i++)
        {
            await fetcher.FetchBlockAsync(21, CancellationToken.None);
        }

        Assert.True(hostA <= 1, $"Host A was contacted {hostA} times; it should be skipped after its first failure.");
    }

    [Fact]
    public async Task Cancellation_Is_Not_Retried()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var fetcher = CreateFetcher(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref calls);
            await cts.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return Ok([]);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchBlockAsync(1, cts.Token));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Every_Host_Is_Tried_Once_Before_Any_Is_Repeated()
    {
        var calls = new List<string>();
        var fetcher = CreateFetcher(
            (request, _) =>
            {
                lock (calls)
                {
                    calls.Add(request.RequestUri!.Host);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            },
            hosts: ["http://a.test:4160", "http://b.test:4160", "http://c.test:4160"],
            maxAttempts: 3);

        await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(4, CancellationToken.None));

        Assert.Equal(["a.test", "b.test", "c.test"], calls.Order());
    }

    [Fact]
    public async Task A_Host_Discovery_Failure_Is_A_Retryable_Fetch_Failure()
    {
        var fetcher = new GossipBlockFetcher(
            new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("must not be called"))),
            new FailingHosts(),
            Chain.Profile,
            new AlgorandChainOptions { Chain = Chain.Profile.Chain },
            Chain.GenesisHash,
            NullLogger<GossipBlockFetcher>.Instance);

        var error = await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(4, CancellationToken.None));

        Assert.IsType<System.Net.Sockets.SocketException>(error.InnerException);
    }

    [Fact]
    public async Task A_Missing_Block_Is_Not_Available_Even_When_Other_Hosts_Merely_Error()
    {
        var fetcher = CreateFetcher(
            (request, _) => Task.FromResult(new HttpResponseMessage(request.RequestUri!.Host == "a.test" ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable)),
            maxAttempts: 4);

        await Assert.ThrowsAsync<BlockNotAvailableException>(() => fetcher.FetchBlockAsync(77, CancellationToken.None));
    }

    [Fact]
    public async Task A_Wrong_Response_Makes_The_Fetch_A_Failure_Rather_Than_Not_Available()
    {
        var fetcher = CreateFetcher(
            (request, _) => Task.FromResult(request.RequestUri!.Host == "a.test" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(Chain.Encode(1, []))),
            maxAttempts: 4);

        var error = await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(2, CancellationToken.None));

        Assert.IsNotType<BlockNotAvailableException>(error);
    }

    private sealed class FailingHosts : IGossipHostProvider
    {
        public Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<string>>(new System.Net.Sockets.SocketException());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
