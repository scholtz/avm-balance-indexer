namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>A response was well-formed msgpack but is not the expected block of the expected chain.</summary>
public sealed class BlockValidationException : Exception
{
    public BlockValidationException(string message)
        : base(message)
    {
    }

    public BlockValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
