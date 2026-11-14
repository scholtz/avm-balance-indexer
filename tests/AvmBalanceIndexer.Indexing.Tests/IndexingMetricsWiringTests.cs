using System.Net;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AvmBalanceIndexer.TestSupport.Scenario;

namespace AvmBalanceIndexer.Indexing.Tests;

/// <summary>The indexing metrics are recorded where the work happens (#170).</summary>
public sealed class IndexingMetricsWiringTests : IDisposable
{
    private static readonly TestChain Chain = new();
    private readonly MetricsCapture _capture = new();

    public void Dispose() => _capture.Dispose();

    private GossipBlockFetcher Fetcher(Func<HttpRequestMessage, HttpResponseMessage> answer, IGossipHostProvider hosts) =>
        new(
            new HttpClient(new Handler(answer)),
            hosts,
            Chain.Profile,
            new AlgorandChainOptions
            {
                Chain = Chain.Profile.Chain,
                MaxAttempts = 2,
                BaseRetryDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero,
                MaxThrottleDelay = TimeSpan.FromMilliseconds(5),
                ThrottleTimeout = TimeSpan.FromSeconds(5),
            },
            Chain.GenesisHash,
            NullLogger<GossipBlockFetcher>.Instance,
            metrics: _capture.Metrics);

    [Fact]
    public async Task A_Served_Block_Records_Its_Duration_As_Archival()
    {
        var fetcher = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Chain.Encode(4, [])) }, new StaticGossipHostProvider(["http://a.test:4160"]));

        await fetcher.FetchBlockAsync(4, CancellationToken.None);

        var served = Assert.Single(_capture.Of("avm.fetch.block.duration"));
        Assert.Equal("archival", served.Tags["source"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "not_found")]
    [InlineData(HttpStatusCode.InternalServerError, "network")]
    public async Task A_Failed_Request_Counts_Its_Reason(HttpStatusCode status, string reason)
    {
        var fetcher = Fetcher(_ => new HttpResponseMessage(status), new StaticGossipHostProvider(["http://a.test:4160"]));

        await Assert.ThrowsAnyAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(4, CancellationToken.None));

        Assert.Equal(2, _capture.Sum("avm.fetch.block.failures", ("source", "archival"), ("reason", reason)));
    }

    [Fact]
    public async Task An_Invalid_Block_Counts_As_Invalid_And_As_A_Decoder_Error()
    {
        var fetcher = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Chain.Encode(5, [])) }, new StaticGossipHostProvider(["http://a.test:4160"]));

        await Assert.ThrowsAnyAsync<BlockFetchException>(() => fetcher.FetchBlockAsync(4, CancellationToken.None));

        Assert.Equal(2, _capture.Sum("avm.fetch.block.failures", ("reason", "invalid")));
        Assert.Equal(1, _capture.Observe("avm.indexer.decoder_errors", ("kind", "block"))); // once for the failed fetch
    }

    [Fact]
    public async Task A_Throttled_Request_Counts_As_Throttled()
    {
        var calls = 0;
        var fetcher = Fetcher(
            _ => Interlocked.Increment(ref calls) == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Chain.Encode(4, [])) },
            new StaticGossipHostProvider(["http://a.test:4160"]));

        await fetcher.FetchBlockAsync(4, CancellationToken.None);

        Assert.Equal(1, _capture.Sum("avm.fetch.block.failures", ("reason", "throttled")));
    }

    [Fact]
    public async Task A_Block_Served_By_A_Relay_Is_Tagged_As_Relay()
    {
        var fetcher = Fetcher(
            request => request.RequestUri!.Host.StartsWith("relay", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Chain.Encode(1000, [])) }
                : new HttpResponseMessage(HttpStatusCode.NotFound),
            new Tiered(["http://archive.test:4160"], ["http://relay.test:4160"]));

        for (var i = 0; i < 100 && _capture.Sum("avm.fetch.block.duration") == 0; i++)
        {
            try
            {
                await fetcher.FetchBlockAsync(1000, 1000, CancellationToken.None);
                break;
            }
            catch (BlockNotAvailableException)
            {
                // The archival node was asked first and the relay was cooled down by an earlier attempt; ask again.
            }
        }

        Assert.All(_capture.Of("avm.fetch.block.duration"), static m => Assert.Equal("relay", m.Tags["source"]));
        Assert.NotEmpty(_capture.Of("avm.fetch.block.duration"));
        Assert.All(_capture.Of("avm.fetch.block.failures"), static m => Assert.Equal("archival", m.Tags["source"])); // only the archival node lacks it
    }

    [Fact]
    public async Task A_Malformed_Arc200_Log_Of_A_Published_Round_Is_Counted_Once()
    {
        // Rounds 1 to 5 of the scenario, then round 6 with an application call that logs a truncated transfer event.
        var log = TestChain.Arc200TransferLog(Alice, Bob, 5);
        byte[] truncated = [.. log[..^1]];
        var chain = new TestChain(); // blocks link through the hashes one chain instance remembers
        var blocks = Blocks(chain).Append(chain.Block(6, [new TestPayment(Alice, Bob, 0, Fee: 1_000, Application: new TestApplication(4242, [truncated]))], rewards: RewardsAt(5)));
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var indexer = new RoundIndexer(Adapter(blocks), new AlgoEffectNormalizer(), storage, Options.Create(new IndexerOptions { FetchConcurrency = 2 }), NullLogger<RoundIndexer>.Instance, metrics: _capture.Metrics);

        await indexer.RunAsync(LastRound + 1, CancellationToken.None);

        Assert.Equal(1, _capture.Observe("avm.indexer.decoder_errors", ("kind", "arc200_log")));
    }

    [Fact]
    public async Task Committed_Batches_Record_Effects_And_Duration()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));
        var indexer = new RoundIndexer(Adapter(), new AlgoEffectNormalizer(), storage, Options.Create(new IndexerOptions { FetchConcurrency = 2 }), NullLogger<RoundIndexer>.Instance, metrics: _capture.Metrics);

        await indexer.RunAsync(LastRound, CancellationToken.None);

        var effects = (int)_capture.Sum("avm.indexer.commit.effects");
        Assert.InRange(effects, 1, storage.EffectCount - 1); // every effect after genesis (genesis is written on its own)
        Assert.NotEmpty(_capture.Of("avm.indexer.commit.duration"));
    }

    [Fact]
    public void A_Log_With_The_Transfer_Selector_But_The_Wrong_Length_Is_Counted_As_Malformed()
    {
        var wellFormed = TestChain.Arc200TransferLog(TestChain.Account(1), TestChain.Account(2), 5);
        byte[] truncated = [.. wellFormed[..^1]];
        byte[] unrelated = [1, 2, 3];
        var block = Chain.Block(5, [new TestPayment(TestChain.Account(1), TestChain.Account(2), 0, Application: new TestApplication(4242, [wellFormed, truncated, unrelated], 0))]);

        var normalized = new AlgoEffectNormalizer().NormalizeBlock(block, default, AssetDirectory.Empty);

        Assert.Equal(2, normalized.Deltas.Count(static d => d.Token == Domain.Tokens.TokenKey.Arc200(4242))); // the well-formed transfer
        Assert.Equal(1, normalized.MalformedArc200Logs);
    }

    private sealed class Tiered(string[] archival, string[] relays) : IGossipHostProvider
    {
        public Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(archival);

        public Task<IReadOnlyList<string>> GetRelayHostsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(relays);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
