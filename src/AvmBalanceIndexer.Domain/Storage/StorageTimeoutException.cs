namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Raised when a read exceeded the provider's read timeout, for example a holder scan over a token that is too large to answer
/// synchronously. The request is abandoned. Holders at the latest round are the cheapest query; for a past round of a very
/// large token only an asynchronous scan (#30) is a reliable way to get an answer.
/// </summary>
public sealed class StorageTimeoutException : Exception
{
    public StorageTimeoutException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
