using Algorand;
using Microsoft.Extensions.Logging;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Builds a ready-to-use <see cref="IChainAdapter"/> from configuration.</summary>
public static class AlgorandChainFactory
{
    /// <summary>
    /// Creates the HTTP client shared by block fetching. A single long-lived client with a pooled handler is the
    /// recommended pattern; per-request timeouts are enforced by the fetcher, so the client timeout is infinite.
    /// </summary>
    public static HttpClient CreateBlockHttpClient() =>
        new(CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Resolves the genesis hash the chain must present, failing closed when none is known.</summary>
    /// <exception cref="InvalidOperationException">Neither the options nor the profile pin a valid genesis hash.</exception>
    public static byte[] ResolveGenesisHash(ChainProfile profile, AlgorandChainOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);

        var configured = string.IsNullOrWhiteSpace(options.ExpectedGenesisHash) ? profile.GenesisHash : options.ExpectedGenesisHash;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Chain {profile.Chain} has no pinned genesis hash. Set Algorand:ExpectedGenesisHash (base64).");
        }

        try
        {
            var hash = Convert.FromBase64String(configured);
            return hash.Length == 32
                ? hash
                : throw new InvalidOperationException("Algorand:ExpectedGenesisHash must decode to 32 bytes.");
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Algorand:ExpectedGenesisHash is not valid base64.", ex);
        }
    }

    /// <summary>Creates the adapter for the configured chain; the adapter owns and disposes its HTTP clients.</summary>
    public static AlgorandChainAdapter Create(AlgorandChainOptions options, ILoggerFactory loggerFactory) =>
        Create(options, loggerFactory, IndexingMetrics.None);

    /// <summary>Creates the adapter for the configured chain, recording block fetches in <paramref name="metrics"/>.</summary>
    public static AlgorandChainAdapter Create(AlgorandChainOptions options, ILoggerFactory loggerFactory, IndexingMetrics metrics)
    {
        var blockHttpClient = CreateBlockHttpClient();
        try
        {
            return CreateCore(options, blockHttpClient, loggerFactory, metrics, [blockHttpClient]);
        }
        catch
        {
            blockHttpClient.Dispose();
            throw;
        }
    }

    /// <summary>Creates the adapter for the configured chain around an existing block HTTP client.</summary>
    /// <param name="options">Chain settings.</param>
    /// <param name="blockHttpClient">HTTP client used for block downloads.</param>
    /// <param name="loggerFactory">Logger source.</param>
    /// <param name="owned">Resources the adapter disposes together with itself.</param>
    public static AlgorandChainAdapter Create(AlgorandChainOptions options, HttpClient blockHttpClient, ILoggerFactory loggerFactory, params IDisposable[] owned) =>
        CreateCore(options, blockHttpClient, loggerFactory, IndexingMetrics.None, owned);

    /// <summary>Creates the ARC-200 metadata source for the configured chain; it uses the same algod URL and token as the adapter and disposes its client.</summary>
    /// <param name="options">Chain settings.</param>
    /// <param name="loggerFactory">Logger source.</param>
    public static AlgodArc200MetadataSource CreateArc200MetadataSource(AlgorandChainOptions options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var profile = ChainProfile.For(options.Chain);
        var http = CreateAlgodClient(options, profile);
        http.Timeout = TimeSpan.FromSeconds(30);

        // A relative request URI replaces the last path segment of a base address without a trailing slash (a gateway such as /algod).
        http.BaseAddress = http.BaseAddress is null ? null : WithTrailingSlash(http.BaseAddress);

        return new AlgodArc200MetadataSource(http, ResolveGenesisHash(profile, options), loggerFactory.CreateLogger<AlgodArc200MetadataSource>(), ownsClient: true);
    }

    /// <summary>Creates the ledger delta source for the configured chain (the ledger audit); it uses the same algod URL and token as the adapter and disposes its client.</summary>
    /// <param name="options">Chain settings.</param>
    /// <param name="loggerFactory">Logger source.</param>
    public static AlgodLedgerDeltaSource CreateLedgerDeltaSource(AlgorandChainOptions options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var http = CreateAlgodClient(options, ChainProfile.For(options.Chain));
        http.Timeout = TimeSpan.FromSeconds(30);
        http.BaseAddress = http.BaseAddress is null ? null : WithTrailingSlash(http.BaseAddress);
        return new AlgodLedgerDeltaSource(http, loggerFactory.CreateLogger<AlgodLedgerDeltaSource>(), ownsClient: true);
    }

    private static HttpClient CreateAlgodClient(AlgorandChainOptions options, ChainProfile profile) =>
        HttpClientConfigurator.ConfigureHttpClient(
            string.IsNullOrWhiteSpace(options.AlgodUrl) ? profile.DefaultAlgodUrl : options.AlgodUrl,
            options.AlgodToken ?? string.Empty,
            string.Empty,
            -1,
            new AlgodQuotaHandler(CreateHandler()));

    internal static Uri WithTrailingSlash(Uri address) =>
        address.AbsolutePath.EndsWith('/') ? address : new UriBuilder(address) { Path = address.AbsolutePath + "/" }.Uri;

    private static AlgorandChainAdapter CreateCore(AlgorandChainOptions options, HttpClient blockHttpClient, ILoggerFactory loggerFactory, IndexingMetrics metrics, IDisposable[] owned)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(blockHttpClient);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var profile = ChainProfile.For(options.Chain);
        var genesisHash = ResolveGenesisHash(profile, options);

        IGossipHostProvider hosts = options.GossipHosts.Length > 0
            ? new StaticGossipHostProvider(options.GossipHosts)
            : new SrvGossipHostProvider(profile.Network, options.HostRefreshInterval, loggerFactory.CreateLogger<SrvGossipHostProvider>());
        HttpClient? algodHttp = null;
        try
        {
            var fetcher = new GossipBlockFetcher(
                blockHttpClient,
                hosts,
                profile,
                options,
                genesisHash,
                loggerFactory.CreateLogger<GossipBlockFetcher>(),
                metrics: metrics);

            algodHttp = CreateAlgodClient(options, profile);
            algodHttp.Timeout = TimeSpan.FromSeconds(60);

            IDisposable[] resources = hosts is IDisposable disposableHosts ? [.. owned, disposableHosts] : owned;
            return new AlgorandChainAdapter(profile, genesisHash, fetcher, algodHttp, resources);
        }
        catch
        {
            algodHttp?.Dispose();
            (hosts as IDisposable)?.Dispose();
            throw;
        }
    }

    private static SocketsHttpHandler CreateHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        MaxConnectionsPerServer = 32,
    };
}
