using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Tests;

public class ChainProfileTests
{
    [Theory]
    [InlineData(ChainId.AlgorandMainnet, "mainnet-v1.0", "wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8=")]
    [InlineData(ChainId.AlgorandTestnet, "testnet-v1.0", "SGO1GKSzyE7IEPItTxCByw9x8FmnrCDexi9/cOUJOiI=")]
    [InlineData(ChainId.Voi, "voimain-v1.0", "r20fSQI8gWe/kFZziNonSPCXLwcQmH/nxROvnnueWOk=")]
    public void Algorand_Networks_Pin_Their_Genesis(string chain, string genesisId, string genesisHash)
    {
        var profile = ChainProfile.For(chain);

        Assert.Equal((genesisId, genesisHash), (profile.GenesisId, profile.GenesisHash));
        Assert.Equal(32, Convert.FromBase64String(profile.GenesisHash!).Length);
    }

    [Fact]
    public void A_Chain_Without_A_Pinned_Genesis_Hash_Must_Be_Configured_Explicitly()
    {
        var unpinned = ChainProfile.For(ChainId.Voi) with { GenesisHash = null };
        var options = new AlgorandChainOptions { Chain = ChainId.Voi };

        var error = Assert.Throws<InvalidOperationException>(() => AlgorandChainFactory.ResolveGenesisHash(unpinned, options));

        Assert.Contains("ExpectedGenesisHash", error.Message, StringComparison.Ordinal);
        options.ExpectedGenesisHash = Convert.ToBase64String(new byte[32]);
        Assert.Equal(new byte[32], AlgorandChainFactory.ResolveGenesisHash(unpinned, options));
    }

    [Theory]
    [InlineData("https://gateway.test/algod")]
    [InlineData("https://gateway.test/algod/")]
    public async Task Genesis_Is_Requested_Below_The_Configured_Algod_Path(string algodUrl)
    {
        Uri? requested = null;
        var handler = new RecordingHandler(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"alloc\":[],\"id\":\"v1.0\",\"network\":\"testnet\"}") };
        });
        using var algod = Algorand.HttpClientConfigurator.ConfigureHttpClient(algodUrl, string.Empty, string.Empty, -1, handler);
        using var adapter = new AlgorandChainAdapter(ChainProfile.For(ChainId.AlgorandTestnet), new byte[32], new NoBlocks(), algod);

        var genesis = await adapter.GetGenesisAsync(CancellationToken.None);

        Assert.Equal("https://gateway.test/algod/genesis", requested!.ToString());
        Assert.Equal("testnet-v1.0", genesis.Id);
    }

    [Fact]
    public async Task Genesis_Request_Survives_Transient_Connection_Resets()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls < 3
            ? throw new HttpRequestException("Connection reset by peer")
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"alloc\":[],\"id\":\"v1.0\",\"network\":\"testnet\"}") });
        using var algod = Algorand.HttpClientConfigurator.ConfigureHttpClient("https://gateway.test/algod/", string.Empty, string.Empty, -1, handler);
        using var adapter = new AlgorandChainAdapter(ChainProfile.For(ChainId.AlgorandTestnet), new byte[32], new NoBlocks(), algod);

        var genesis = await adapter.GetGenesisAsync(CancellationToken.None);

        Assert.Equal("testnet-v1.0", genesis.Id);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Genesis_Request_Gives_Up_After_Repeated_Failures()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => throw new HttpRequestException($"reset {++calls}"));
        using var algod = Algorand.HttpClientConfigurator.ConfigureHttpClient("https://gateway.test/algod/", string.Empty, string.Empty, -1, handler);
        using var adapter = new AlgorandChainAdapter(ChainProfile.For(ChainId.AlgorandTestnet), new byte[32], new NoBlocks(), algod);

        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.GetGenesisAsync(CancellationToken.None));

        Assert.Equal(4, calls);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class NoBlocks : IBlockFetcher
    {
        public Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public void Unknown_Chains_Are_Rejected()
    {
        Assert.Throws<ArgumentException>(() => ChainProfile.For("solana"));
    }

    [Fact]
    public void A_Configured_Genesis_Hash_Wins_Over_The_Pinned_One()
    {
        var configured = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

        var hash = AlgorandChainFactory.ResolveGenesisHash(
            ChainProfile.For(ChainId.AlgorandTestnet),
            new AlgorandChainOptions { Chain = ChainId.AlgorandTestnet, ExpectedGenesisHash = configured });

        Assert.Equal(Convert.FromBase64String(configured), hash);
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    public void A_Malformed_Genesis_Hash_Is_Rejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => AlgorandChainFactory.ResolveGenesisHash(
            ChainProfile.For(ChainId.AlgorandTestnet),
            new AlgorandChainOptions { Chain = ChainId.AlgorandTestnet, ExpectedGenesisHash = value }));
    }

    [Fact]
    public void Chain_Ids_Are_Recognized_Exactly()
    {
        Assert.All(ChainId.All, chain => Assert.True(ChainId.IsKnown(chain)));
        Assert.False(ChainId.IsKnown("VOI"));
        Assert.Throws<ArgumentException>(() => new ChainContext("voi2"));
    }
}
