using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Storage.ContractTests;

/// <summary>The in-memory reference provider must satisfy the same contract as the real ones.</summary>
public class InMemoryContractTests : StorageContractTests
{
    protected override Task<IChainStorage> CreateStorageAsync() =>
        Task.FromResult<IChainStorage>(new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet)));
}
