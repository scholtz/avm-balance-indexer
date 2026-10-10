using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>An in-memory chain adapter serving pre-built blocks, for hermetic indexer tests.</summary>
public sealed class FakeChainAdapter(ChainProfile profile, GenesisDocument genesis) : IChainAdapter
{
    private readonly Dictionary<ulong, BlockData> _blocks = [];
    private int _fetchCount;

    public ChainProfile Profile { get; } = profile;

    /// <summary>The round reported as latest; defaults to the highest added block.</summary>
    public ulong? Latest { get; set; }

    /// <summary>Number of block fetches served.</summary>
    public int FetchCount => Volatile.Read(ref _fetchCount);

    public FakeChainAdapter Add(BlockData block)
    {
        _blocks[block.Round] = block;
        return this;
    }

    public Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Latest ?? (_blocks.Count == 0 ? 0UL : _blocks.Keys.Max()));

    public Task<GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken) => Task.FromResult(genesis);

    public Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _fetchCount);
        return _blocks.TryGetValue(round, out var block)
            ? Task.FromResult(block)
            : Task.FromException<BlockData>(new BlockNotAvailableException(round));
    }
}
