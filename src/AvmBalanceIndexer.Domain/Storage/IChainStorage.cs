namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>Everything one provider implements for one chain deployment.</summary>
public interface IChainStorage : IChainStatusStore, ITokenStore, IBalanceReadStore, IRoundWriter, ISchemaManager
{
}
