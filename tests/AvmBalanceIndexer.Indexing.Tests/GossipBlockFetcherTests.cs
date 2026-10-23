using System.Collections.Concurrent;
using System.Net;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging;
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
        TimeProvider? time = null,
        int maxConcurrentPerHost = 32,
        TimeSpan? throttleTimeout = null,
        ILogger<GossipBlockFetcher>? logger = null,
        IGossipHostProvider? provider = null,
        int recentRoundsWindow = 100)
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
            MaxConcurrentRequestsPerHost = maxConcurrentPerHost,
            MaxThrottleDelay = TimeSpan.FromMilliseconds(20),
            ThrottleTimeout = throttleTimeout ?? TimeSpan.FromSeconds(5),
            RecentRoundsWindow = recentRoundsWindow,
        };

        return new GossipBlockFetcher(
            new HttpClient(new StubHandler(handler)),
            provider ?? new StaticGossipHostProvider(hosts ?? [HostA, HostB]),
            Chain.Profile,
            options,
            Chain.GenesisHash,
            logger ?? NullLogger<GossipBlockFetcher>.Instance,
            time);
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Throttled(TimeSpan? retryAfter = null, HttpStatusCode status = HttpStatusCode.TooManyRequests)
    {
        var response = new HttpResponseMessage(status);
        if (retryAfter is { } delay)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
        }

        return response;
    }

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
    public async Task Fast_Hosts_Get_Most_Of_The_Requests()
    {
        var served = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var clock = new ManualClock(); // response times come from a manual clock, so machine load cannot skew them
        var fetcher = CreateFetcher(
            (request, token) =>
            {
                var host = request.RequestUri!.Host;
                served.AddOrUpdate(host, 1, static (_, n) => n + 1);
                clock.Advance(TimeSpan.FromMilliseconds(host == "slow.test" ? 200 : 5));
                return Task.FromResult(Ok(Chain.Encode(7, [])));
            },
            hosts: ["http://slow.test:4160", "http://fast.test:4160"],
            time: clock);

        for (var i = 0; i < 80; i++)
        {
            await fetcher.FetchBlockAsync(7, CancellationToken.None);
        }

        Assert.True(served["fast.test"] >= 68, $"fast {served["fast.test"]}, slow {served.GetValueOrDefault("slow.test")}");
    }

    [Fact]
    public async Task A_Host_Without_The_Block_Is_Not_Preferred_Because_It_Answers_Quickly()
    {
        var served = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var fetcher = CreateFetcher(
            async (request, token) =>
            {
                var host = request.RequestUri!.Host;
                served.AddOrUpdate(host, 1, static (_, n) => n + 1);
                await Task.Delay(host == "empty.test" ? 0 : 30, token);
                return host == "empty.test" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(Chain.Encode(7, []));
            },
            hosts: ["http://empty.test:4160", "http://full.test:4160"]);

        for (var i = 0; i < 60; i++)
        {
            await fetcher.FetchBlockAsync(7, CancellationToken.None);
        }

        Assert.True(served["full.test"] >= 60);
        Assert.True(served.GetValueOrDefault("empty.test") <= 24, $"empty {served.GetValueOrDefault("empty.test")}");
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
    public async Task A_Malformed_Host_Is_Skipped_Instead_Of_Failing_The_Fetch()
    {
        var fetcher = CreateFetcher(
            (_, _) => Task.FromResult(Ok(Chain.Encode(9, []))),
            hosts: ["not a url", "http://b.test:4160"],
            maxAttempts: 4);

        var block = await fetcher.FetchBlockAsync(9, CancellationToken.None);

        Assert.Equal(9UL, block.Round);
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
    public async Task Mostly_Errors_Mean_A_Broken_Fleet_Not_A_Block_That_Is_Not_Out_Yet()
    {
        var calls = 0;
        var fetcher = CreateFetcher(
            (_, _) => Task.FromResult(new HttpResponseMessage(Interlocked.Increment(ref calls) == 1 ? HttpStatusCode.NotFound : HttpStatusCode.InternalServerError)),
            maxAttempts: 6);

        var error = await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(5, CancellationToken.None));

        Assert.IsNotType<BlockNotAvailableException>(error);
    }

    [Fact]
    public async Task One_Wrong_Host_Among_Many_That_Lack_The_Block_Still_Means_Not_Available_Yet()
    {
        var fetcher = CreateFetcher(
            (request, _) => Task.FromResult(request.RequestUri!.Host == "c.test" ? Ok(Chain.Encode(1, [])) : new HttpResponseMessage(HttpStatusCode.NotFound)),
            hosts: ["http://a.test:4160", "http://b.test:4160", "http://c.test:4160"],
            maxAttempts: 3);

        await Assert.ThrowsAsync<BlockNotAvailableException>(() => fetcher.FetchBlockAsync(2, CancellationToken.None));
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

    private sealed class TieredHosts(string[] archival, string[] relays, bool relaysFail = false) : IGossipHostProvider
    {
        public Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(archival);

        public Task<IReadOnlyList<string>> GetRelayHostsAsync(CancellationToken cancellationToken) =>
            relaysFail ? Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("no relays")) : Task.FromResult<IReadOnlyList<string>>(relays);
    }

    [Fact]
    public async Task Blocks_Near_The_Tip_Are_Also_Fetched_From_Relays()
    {
        var served = new ConcurrentDictionary<string, int>();
        var fetcher = CreateFetcher(
            (request, _) =>
            {
                served.AddOrUpdate(request.RequestUri!.Host, 1, static (_, n) => n + 1);
                return Task.FromResult(Ok(Chain.Encode(950, [])));
            },
            provider: new TieredHosts(["http://archive.test:4160"], ["http://relay.test:4160"]));

        for (var i = 0; i < 40; i++)
        {
            await fetcher.FetchBlockAsync(950, 1000, CancellationToken.None);
        }

        Assert.True(served.GetValueOrDefault("relay.test") > 0, "relays were never asked");
        Assert.True(served.GetValueOrDefault("archive.test") > 0, "archival nodes were never asked");
    }

    [Fact]
    public async Task Old_Blocks_Are_Never_Asked_From_Relays()
    {
        var served = new ConcurrentDictionary<string, int>();
        var fetcher = CreateFetcher(
            (request, _) =>
            {
                served.AddOrUpdate(request.RequestUri!.Host, 1, static (_, n) => n + 1);
                return Task.FromResult(Ok(Chain.Encode(500, [])));
            },
            provider: new TieredHosts(["http://archive.test:4160"], ["http://relay.test:4160"]));

        for (var i = 0; i < 20; i++)
        {
            await fetcher.FetchBlockAsync(500, 1000, CancellationToken.None); // 500 behind the tip
            await fetcher.FetchBlockAsync(500, CancellationToken.None); // no tip known
        }

        Assert.Equal(0, served.GetValueOrDefault("relay.test"));
    }

    [Fact]
    public async Task A_Zero_Window_Turns_Relays_Off()
    {
        var served = new ConcurrentDictionary<string, int>();
        var fetcher = CreateFetcher(
            (request, _) =>
            {
                served.AddOrUpdate(request.RequestUri!.Host, 1, static (_, n) => n + 1);
                return Task.FromResult(Ok(Chain.Encode(1000, [])));
            },
            provider: new TieredHosts(["http://archive.test:4160"], ["http://relay.test:4160"]),
            recentRoundsWindow: 0);

        for (var i = 0; i < 20; i++)
        {
            await fetcher.FetchBlockAsync(1000, 1000, CancellationToken.None);
        }

        Assert.Equal(0, served.GetValueOrDefault("relay.test"));
    }

    [Fact]
    public async Task A_Relay_Discovery_Failure_Leaves_The_Archival_Nodes_Working()
    {
        var fetcher = CreateFetcher(
            (_, _) => Task.FromResult(Ok(Chain.Encode(990, []))),
            provider: new TieredHosts(["http://archive.test:4160"], [], relaysFail: true));

        var block = await fetcher.FetchBlockAsync(990, 1000, CancellationToken.None);

        Assert.Equal(990UL, block.Round);
    }

    [Fact]
    public async Task A_Relay_That_Lacks_The_Newest_Block_Still_Means_Not_Available_Yet()
    {
        var fetcher = CreateFetcher(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            maxAttempts: 4,
            provider: new TieredHosts(["http://archive.test:4160"], ["http://relay.test:4160"]));

        await Assert.ThrowsAsync<BlockNotAvailableException>(() => fetcher.FetchBlockAsync(1000, 1000, CancellationToken.None));
    }

    private sealed class FailingHosts : IGossipHostProvider
    {
        public Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<string>>(new System.Net.Sockets.SocketException());
    }

    [Fact]
    public async Task A_429_Moves_The_Fetch_To_Another_Host_Without_Using_An_Attempt()
    {
        var fetcher = CreateFetcher(
            (request, _) => Task.FromResult(request.RequestUri!.Host == "a.test" ? Throttled(TimeSpan.FromSeconds(1)) : Ok(Chain.Encode(3, []))),
            maxAttempts: 1);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(3UL, (await fetcher.FetchBlockAsync(3, CancellationToken.None)).Round);
        }
    }

    [Fact]
    public async Task A_503_With_Retry_After_Is_Throttling_Too()
    {
        var fetcher = CreateFetcher(
            (request, _) => Task.FromResult(request.RequestUri!.Host == "a.test" ? Throttled(TimeSpan.FromSeconds(1), HttpStatusCode.ServiceUnavailable) : Ok(Chain.Encode(3, []))),
            maxAttempts: 1);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(3UL, (await fetcher.FetchBlockAsync(3, CancellationToken.None)).Round);
        }
    }

    [Fact]
    public async Task Many_Rate_Limits_Produce_One_Information_Line_And_Debug_Lines_For_Each_Pause()
    {
        var logger = new CapturingLogger();
        var calls = 0;
        var fetcher = CreateFetcher((_, _) => Task.FromResult(Interlocked.Increment(ref calls) <= 2 ? Throttled() : Ok(Chain.Encode(4, []))), logger: logger);

        await fetcher.FetchBlockAsync(4, CancellationToken.None);

        var info = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("1 pause", info.Message, StringComparison.Ordinal);
        Assert.Contains(".test", info.Message, StringComparison.Ordinal);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("rate limiting", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_Fetch_That_Fails_Because_Of_Rate_Limits_Reports_The_Held_Back_Pauses_First()
    {
        var logger = new CapturingLogger();
        var fetcher = CreateFetcher((_, _) => Task.FromResult(Throttled()), throttleTimeout: TimeSpan.FromMilliseconds(150), logger: logger);

        await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(4, CancellationToken.None));

        var info = logger.Entries.Where(e => e.Level == LogLevel.Information).ToList();
        Assert.Equal(2, info.Count); // the first pause at once, the held-back one when the fetch gives up
        Assert.Contains("within", info[1].Message, StringComparison.Ordinal);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public override DateTimeOffset GetUtcNow() => new(new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc).Ticks + Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    private sealed class CapturingLogger : ILogger<GossipBlockFetcher>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task When_Every_Host_Throttles_The_Fetch_Waits_Instead_Of_Failing()
    {
        var calls = 0;
        var fetcher = CreateFetcher((_, _) => Task.FromResult(Interlocked.Increment(ref calls) <= 6 ? Throttled() : Ok(Chain.Encode(4, []))), maxAttempts: 2);

        var block = await fetcher.FetchBlockAsync(4, CancellationToken.None);

        Assert.Equal(4UL, block.Round);
        Assert.Equal(7, calls);
    }

    [Fact]
    public async Task Throttling_For_Longer_Than_The_Timeout_Fails_The_Fetch_With_A_Clear_Message()
    {
        var fetcher = CreateFetcher((_, _) => Task.FromResult(Throttled()), throttleTimeout: TimeSpan.FromMilliseconds(250));

        var error = await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(5, CancellationToken.None));

        Assert.Contains("rate limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5UL, error.Round);
    }

    [Fact]
    public async Task The_Timeout_Counts_The_Time_Spent_Waiting_Not_The_Time_Since_The_First_Rate_Limit()
    {
        // A rate limit, slow answers and another rate limit that together take longer than the timeout: the fetch must still succeed.
        var calls = 0;
        var fetcher = CreateFetcher(
            async (_, token) =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    return Throttled();
                }

                await Task.Delay(150, token);
                return call switch
                {
                    2 => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                    3 => Throttled(), // 300 ms after the first rate limit: more than the 250 ms timeout, but hardly any of it spent waiting
                    _ => Ok(Chain.Encode(4, [])),
                };
            },
            maxAttempts: 6,
            throttleTimeout: TimeSpan.FromMilliseconds(250));

        var block = await fetcher.FetchBlockAsync(4, CancellationToken.None);

        Assert.Equal(4UL, block.Round);
    }

    [Fact]
    public async Task A_Fetch_That_Bounces_Between_Rate_Limiting_Nodes_Is_Bounded_By_The_Timeout()
    {
        // Each node pauses for a few milliseconds only (Retry-After: 0), so one of them is usually free again at the next acquire.
        var calls = 0;
        var fetcher = CreateFetcher(
            async (_, token) =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(5, token);
                return Throttled(TimeSpan.Zero);
            },
            throttleTimeout: TimeSpan.FromMilliseconds(300));
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(5, CancellationToken.None));

        Assert.InRange(System.Diagnostics.Stopwatch.GetElapsedTime(started), TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(3));
        Assert.True(calls > 2);
    }

    [Fact]
    public async Task The_Wait_Never_Overshoots_The_Timeout_By_A_Whole_Pause()
    {
        var options = new AlgorandChainOptions
        {
            Chain = Chain.Profile.Chain,
            MaxAttempts = 4,
            BaseRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            MaxThrottleDelay = TimeSpan.FromSeconds(30),
            ThrottleTimeout = TimeSpan.FromMilliseconds(300),
        };
        var fetcher = new GossipBlockFetcher(
            new HttpClient(new StubHandler((_, _) => Task.FromResult(Throttled(TimeSpan.FromSeconds(30))))),
            new StaticGossipHostProvider([HostA]),
            Chain.Profile,
            options,
            Chain.GenesisHash,
            NullLogger<GossipBlockFetcher>.Instance);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(5, CancellationToken.None));

        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5), "The fetch slept through a whole 30 s pause although its timeout is 300 ms.");
    }

    [Fact]
    public async Task A_Rate_Limit_Does_Not_Hide_The_Real_Failure_Of_A_Fetch_That_Runs_Out_Of_Attempts()
    {
        // Host A is rate limited once; every other answer is a server error. The error, not the 429, explains why the fetch failed.
        var calls = 0;
        var fetcher = CreateFetcher(
            (_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? Throttled() : new HttpResponseMessage(HttpStatusCode.BadGateway)),
            maxAttempts: 3);

        var error = await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(5, CancellationToken.None));

        var cause = Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Contains("502", cause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Failure_After_Throttling_Keeps_The_Last_Rate_Limit_As_Its_Cause()
    {
        var fetcher = CreateFetcher((_, _) => Task.FromResult(Throttled()), throttleTimeout: TimeSpan.FromMilliseconds(150));

        var error = await Assert.ThrowsAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(5, CancellationToken.None));

        Assert.NotNull(error.InnerException);
        Assert.Contains("rate limiting", error.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retry_After_Pauses_The_Host_Before_It_Is_Asked_Again()
    {
        var calls = 0;
        var fetcher = CreateFetcher(
            (_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? Throttled(TimeSpan.FromSeconds(30)) : Ok(Chain.Encode(6, []))),
            hosts: [HostA]);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        await fetcher.FetchBlockAsync(6, CancellationToken.None);

        // The 30 s of the header are capped by MaxThrottleDelay (20 ms in these tests), but the host is not asked again at once.
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(15));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_Retry_After_Date_Is_Understood()
    {
        var calls = 0;
        var fetcher = CreateFetcher(
            (_, _) =>
            {
                if (Interlocked.Increment(ref calls) > 1)
                {
                    return Task.FromResult(Ok(Chain.Encode(6, [])));
                }

                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(1));
                return Task.FromResult(response);
            },
            hosts: [HostA]);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        await fetcher.FetchBlockAsync(6, CancellationToken.None);

        Assert.InRange(System.Diagnostics.Stopwatch.GetElapsedTime(started), TimeSpan.FromMilliseconds(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_Number_Of_Simultaneous_Requests_To_A_Host_Is_Capped()
    {
        var current = 0;
        var highest = 0;
        var fetcher = CreateFetcher(
            async (_, token) =>
            {
                var now = Interlocked.Increment(ref current);
                int seen;
                while (now > (seen = Volatile.Read(ref highest)) && Interlocked.CompareExchange(ref highest, now, seen) != seen)
                {
                }

                await Task.Delay(20, token);
                Interlocked.Decrement(ref current);
                return Ok(Chain.Encode(5, []));
            },
            hosts: [HostA],
            maxConcurrentPerHost: 3);

        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => fetcher.FetchBlockAsync(5, CancellationToken.None)));

        Assert.InRange(highest, 1, 3);
    }

    [Fact]
    public async Task A_Host_That_Rate_Limits_Above_Some_Concurrency_Still_Serves_Every_Fetch()
    {
        var current = 0;
        var rejected = 0;
        var fetcher = CreateFetcher(
            async (_, token) =>
            {
                var now = Interlocked.Increment(ref current);
                try
                {
                    await Task.Delay(15, token);
                    if (now > 2)
                    {
                        Interlocked.Increment(ref rejected);
                        return Throttled(TimeSpan.FromSeconds(1));
                    }

                    return Ok(Chain.Encode(8, []));
                }
                finally
                {
                    Interlocked.Decrement(ref current);
                }
            },
            hosts: [HostA],
            maxAttempts: 2,
            maxConcurrentPerHost: 16);

        // Without throttling every request that finds the node busy burns one of the two attempts and the fetch fails.
        var blocks = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => fetcher.FetchBlockAsync(8, CancellationToken.None)));

        Assert.All(blocks, static b => Assert.Equal(8UL, b.Round));
        Assert.True(rejected > 0, "The test host never rate limited, so it proves nothing.");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
