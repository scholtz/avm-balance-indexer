using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// An in-memory chain adapter serving pre-built blocks, for hermetic indexer tests. Thread-safe: tests add blocks and move the tip while
/// the indexer's prefetch threads read.
/// </summary>
public sealed class FakeChainAdapter(ChainProfile profile, GenesisDocument genesis) : IChainAdapter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, BlockData> _blocks = [];
    private readonly object _latestGate = new();
    private ulong? _latest;
    private int _fetchCount;
    private bool _algodDown;

    public ChainProfile Profile { get; } = profile;

    public ReadOnlyMemory<byte> GenesisHash { get; } = Convert.FromBase64String(profile.GenesisHash!);

    /// <summary>The round reported as latest; defaults to the highest added block.</summary>
    public ulong? Latest
    {
        get
        {
            lock (_latestGate)
            {
                return _latest;
            }
        }

        set
        {
            lock (_latestGate)
            {
                _latest = value;
            }
        }
    }

    /// <summary>When true, asking for the latest round fails like an unreachable algod would.</summary>
    public bool AlgodDown
    {
        get => Volatile.Read(ref _algodDown);
        set => Volatile.Write(ref _algodDown, value);
    }

    /// <summary>Number of block fetches served.</summary>
    public int FetchCount => Volatile.Read(ref _fetchCount);

    public FakeChainAdapter Add(BlockData block)
    {
        _blocks[block.Round] = block;
        return this;
    }

    public Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken) =>
        AlgodDown
            ? Task.FromException<ulong>(new HttpRequestException("algod is unreachable"))
            : Task.FromResult(Latest ?? (_blocks.IsEmpty ? 0UL : _blocks.Keys.Max()));

    public Task<GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken) => Task.FromResult(genesis);

    /// <summary>The latest-round hints of the fetches that passed one, in the order they were made.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<ulong> TipHints { get; } = new();

    public Task<BlockData> FetchBlockAsync(ulong round, ulong latestRound, CancellationToken cancellationToken)
    {
        TipHints.Enqueue(latestRound);
        return FetchBlockAsync(round, cancellationToken);
    }

    public Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _fetchCount);
        return _blocks.TryGetValue(round, out var block)
            ? Task.FromResult(block)
            : Task.FromException<BlockData>(new BlockNotAvailableException(round));
    }
}
