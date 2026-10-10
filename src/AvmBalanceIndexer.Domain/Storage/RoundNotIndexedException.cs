namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Raised when a read asks for a round beyond the published indexed round.</summary>
public sealed class RoundNotIndexedException : Exception
{
    public RoundNotIndexedException(ulong requestedRound, ulong indexedRound)
        : base($"Round {requestedRound} is not indexed yet (indexed round is {indexedRound}).")
    {
        RequestedRound = requestedRound;
        IndexedRound = indexedRound;
    }

    public ulong RequestedRound { get; }

    public ulong IndexedRound { get; }
}
