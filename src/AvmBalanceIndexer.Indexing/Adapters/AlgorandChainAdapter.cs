using Algorand.Algod;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Algorand-family chain adapter. Latest round comes from an algod node through the Algorand5 <see cref="AlgodClient"/>;
/// blocks come from archival gossip nodes through an <see cref="IBlockFetcher"/>.
/// </summary>
public sealed class AlgorandChainAdapter : IChainAdapter, IDisposable
{
    private readonly IBlockFetcher _blocks;
    private const int GenesisAttempts = 4;

    private readonly HttpClient _algodHttp;
    private readonly AlgodClient _algod;
    private readonly IDisposable[] _owned;

    /// <param name="profile">Chain this adapter serves.</param>
    /// <param name="genesisHash">Genesis hash the chain must have.</param>
    /// <param name="blocks">Archival block source.</param>
    /// <param name="algodHttp">HTTP client with base address and token already configured for the algod node; disposed with the adapter.</param>
    /// <param name="owned">Further resources (for example the block HTTP client) that are disposed with the adapter.</param>
    public AlgorandChainAdapter(ChainProfile profile, byte[] genesisHash, IBlockFetcher blocks, HttpClient algodHttp, params IDisposable[] owned)
    {
        GenesisHash = genesisHash ?? throw new ArgumentNullException(nameof(genesisHash));
        _owned = owned ?? [];
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _algodHttp = algodHttp ?? throw new ArgumentNullException(nameof(algodHttp));
        _algod = new AlgodClient(algodHttp);
    }

    public ChainProfile Profile { get; }

    public ReadOnlyMemory<byte> GenesisHash { get; }

    public async Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken)
    {
        var status = await _algod.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.LastRound;
    }

    public async Task<GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken)
    {
        // The SDK Genesis model types the account state as raw bytes, so balances are read from the JSON directly.
        // A pooled connection the server has closed fails the first request with a reset: ask again on a fresh one.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var json = await _algodHttp.GetStringAsync(new Uri("genesis", UriKind.Relative), cancellationToken).ConfigureAwait(false);
                return GenesisDocument.Parse(json);
            }
            catch (HttpRequestException) when (attempt < GenesisAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken) =>
        _blocks.FetchBlockAsync(round, cancellationToken);

    public void Dispose()
    {
        _algodHttp.Dispose();
        foreach (var resource in _owned)
        {
            resource.Dispose();
        }
    }
}
