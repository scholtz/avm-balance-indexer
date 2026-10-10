using System.Collections.Concurrent;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Wraps a real adapter and remembers blocks, so several providers can be indexed from one download of the
/// same rounds without hammering the public archival nodes.
/// </summary>
public sealed class CachedChainAdapter(IChainAdapter inner) : IChainAdapter
{
    private readonly ConcurrentDictionary<ulong, Task<BlockData>> _blocks = new();

    public ChainProfile Profile => inner.Profile;

    public Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken) => inner.GetLatestRoundAsync(cancellationToken);

    public Task<GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken) => inner.GetGenesisAsync(cancellationToken);

    public Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken)
    {
        var task = _blocks.GetOrAdd(round, r => inner.FetchBlockAsync(r, CancellationToken.None));
        if (task.IsFaulted)
        {
            _blocks.TryRemove(round, out _);
        }

        return task.WaitAsync(cancellationToken);
    }
}
