namespace AvmBalanceIndexer.Domain.Chains;

/// <summary>The chain a deployment serves; one process serves exactly one chain.</summary>
public sealed record ChainContext
{
    public ChainContext(string chain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chain);
        Chain = ChainId.IsKnown(chain)
            ? chain
            : throw new ArgumentException($"Unknown chain {chain}.", nameof(chain));
    }

    public string Chain { get; }
}
