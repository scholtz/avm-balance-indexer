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
    public static AlgorandChainAdapter Create(AlgorandChainOptions options, ILoggerFactory loggerFactory)
    {
        var blockHttpClient = CreateBlockHttpClient();
        try
        {
            return Create(options, blockHttpClient, loggerFactory, blockHttpClient);
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
    public static AlgorandChainAdapter Create(AlgorandChainOptions options, HttpClient blockHttpClient, ILoggerFactory loggerFactory, params IDisposable[] owned)
    {
        ArgumentNullException.ThrowIfNull(options);
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
                loggerFactory.CreateLogger<GossipBlockFetcher>());

            algodHttp = HttpClientConfigurator.ConfigureHttpClient(
                string.IsNullOrWhiteSpace(options.AlgodUrl) ? profile.DefaultAlgodUrl : options.AlgodUrl,
                options.AlgodToken ?? string.Empty,
                string.Empty,
                -1,
                CreateHandler());
            algodHttp.Timeout = TimeSpan.FromSeconds(60);

            IDisposable[] resources = hosts is IDisposable disposableHosts ? [.. owned, disposableHosts] : owned;
            return new AlgorandChainAdapter(profile, fetcher, algodHttp, resources);
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
