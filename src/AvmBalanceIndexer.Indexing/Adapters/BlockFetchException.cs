namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>All attempts to fetch a block failed.</summary>
public class BlockFetchException : Exception
{
    public BlockFetchException(string message, ulong round, Exception? innerException = null)
        : base(message, innerException)
    {
        Round = round;
    }

    public ulong Round { get; }
}

/// <summary>
/// No reachable archival node had the block. At the chain tip this is expected until the block propagates;
/// callers may retry later.
/// </summary>
public sealed class BlockNotAvailableException(ulong round)
    : BlockFetchException($"No archival node has block {round}.", round);
