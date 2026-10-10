namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Raised by reads when the indexer has not published anything for the chain yet.</summary>
public sealed class ChainNotStartedException(string chain)
    : InvalidOperationException($"Chain {chain} has not been indexed yet.")
{
    public string Chain { get; } = chain;
}
