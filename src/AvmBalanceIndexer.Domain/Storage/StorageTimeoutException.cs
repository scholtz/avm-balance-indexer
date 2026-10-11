namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Raised when a read exceeded the provider's read timeout, for example a holder scan over a token that is too large to answer
/// synchronously. The request is abandoned; asking for a smaller page or a later round does not help, a scan job does.
/// </summary>
public sealed class StorageTimeoutException : Exception
{
    public StorageTimeoutException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
