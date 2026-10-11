using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.TestSupport;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// The same proof as <see cref="FirstThousandRoundsTests"/> for the Cassandra provider: the first 1000 rounds of real chains are
/// indexed into a keyspace and every balance of every round is compared with the ledger state deltas of a public algod.
/// </summary>
[Collection(CassandraServerDefinition.Name)]
public class FirstThousandRoundsCassandraTests(CassandraFixture cassandra)
{
    [LiveFact]
    public async Task TestNet_First_1000_Rounds_Match_The_Ledger_In_Cassandra()
    {
        var storage = await cassandra.CreateStorageAsync(ChainId.AlgorandTestnet);

        await FirstThousandRoundsTests.IndexAndVerifyAsync(ChainId.AlgorandTestnet, storage, expectPayments: true);
    }

    [LiveFact]
    public async Task MainNet_First_1000_Rounds_Match_The_Ledger_In_Cassandra()
    {
        var storage = await cassandra.CreateStorageAsync(ChainId.AlgorandMainnet);

        await FirstThousandRoundsTests.IndexAndVerifyAsync(ChainId.AlgorandMainnet, storage, expectPayments: false);
    }
}
