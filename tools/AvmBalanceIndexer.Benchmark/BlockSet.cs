using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvmBalanceIndexer.Benchmark;

/// <summary>
/// The newest rounds of a chain, downloaded once and replayed as rounds 1 to N of an empty database. The indexer needs a chain
/// that starts at genesis, so the first block of the range is renumbered to round 1 and the starting balances are seeded
/// (see <see cref="Seeder"/>). Transaction ids, accounts and amounts stay the real ones.
/// </summary>
public sealed class BlockSet : IChainAdapter
{
    private readonly BlockData[] _blocks; // index 0 is the block before the range, kept for its hash

    private BlockSet(ChainProfile profile, BlockData[] blocks, ulong firstChainRound)
    {
        Profile = profile;
        _blocks = blocks;
        FirstChainRound = firstChainRound;
    }

    public ChainProfile Profile { get; }

    public ReadOnlyMemory<byte> GenesisHash => Convert.FromBase64String(Profile.GenesisHash!);

    /// <summary>Chain round that was renumbered to round 1.</summary>
    public ulong FirstChainRound { get; }

    public int Rounds => _blocks.Length - 1;

    public IEnumerable<BlockData> Blocks => _blocks.Skip(1);

    public long Transactions => Blocks.Sum(static b => (long)b.ApplyData.Count);

    public static async Task<(BlockSet Set, DownloadResult Download)> DownloadAsync(string chain, int rounds, ulong marginBehindTip, ulong? lastRound, CancellationToken cancellationToken)
    {
        var http = AlgorandChainFactory.CreateBlockHttpClient();
        var options = new AlgorandChainOptions { Chain = chain, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
        var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);

        var tip = await adapter.GetLatestRoundAsync(cancellationToken).ConfigureAwait(false);
        var last = lastRound ?? tip - marginBehindTip;
        var first = last - (ulong)rounds + 1;
        Console.WriteLine($"Downloading {rounds} rounds of {chain}: {first} to {last} (tip {tip})");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var downloaded = new List<BlockData>(rounds + 1);
        await foreach (var block in OrderedPrefetch.StreamAsync(first - 1, last, 128, adapter.FetchBlockAsync, cancellationToken).ConfigureAwait(false))
        {
            downloaded.Add(block);
        }

        clock.Stop();
        var set = FromBlocks(adapter.Profile, downloaded, first);
        var result = new DownloadResult(clock.Elapsed.TotalSeconds, rounds / clock.Elapsed.TotalSeconds, set.Transactions);
        Console.WriteLine($"Downloaded in {result.Seconds:F1} s ({result.RoundsPerSecond:F0} rounds/s, {result.Transactions} transactions)");
        return (set, result);
    }

    /// <summary>Builds the replay from consecutive blocks; the first one is the block before the range.</summary>
    public static BlockSet FromBlocks(ChainProfile profile, IReadOnlyList<BlockData> consecutive, ulong firstChainRound)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(consecutive);
        var blocks = new BlockData[consecutive.Count];
        for (var i = 0; i < blocks.Length; i++)
        {
            // Round i of the replay links to round i-1; round 1 links to the real block before the range.
            blocks[i] = i == 0 ? consecutive[0] : consecutive[i] with { Round = (ulong)i, PreviousHash = consecutive[i - 1].Hash };
        }

        return new BlockSet(profile, blocks, firstChainRound);
    }

    /// <summary>Rewards state at the end of the real block before the range.</summary>
    public Indexing.Normalization.RewardsState RewardsBeforeFirstRound => Indexing.Normalization.RewardsState.FromHeader(_blocks[0].Block);

    /// <summary>The hash the replayed round 1 links back to.</summary>
    public byte[] BeforeFirstHash => _blocks[0].Hash;

    public Task<ulong> GetLatestRoundAsync(CancellationToken cancellationToken) => Task.FromResult((ulong)Rounds);

    public Task<GenesisDocument> GetGenesisAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("The replay starts from a seeded round 0 that is written directly.");

    public Task<BlockData> FetchBlockAsync(ulong round, CancellationToken cancellationToken) =>
        round >= 1 && round <= (ulong)Rounds
            ? Task.FromResult(_blocks[(int)round])
            : Task.FromException<BlockData>(new BlockNotAvailableException(round));
}
