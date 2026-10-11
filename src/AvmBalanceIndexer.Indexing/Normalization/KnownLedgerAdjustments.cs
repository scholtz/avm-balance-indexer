using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>
/// Balance movements the ledger performs without any transaction. TestNet received two manual hotfixes that moved
/// funds from the bank (dispenser) account to the rewards pool at the start of a round; they are protocol history
/// and must be replayed to get exact balances.
/// </summary>
public static class KnownLedgerAdjustments
{
    private const string TestNetGenesisHash = "SGO1GKSzyE7IEPItTxCByw9x8FmnrCDexi9/cOUJOiI=";
    private const string TestNetBank = "GD64YIY3TWGDMCNPP553DZPPR6LDUSFQOIJVFDPPXWEG3FVOJCCDBBHU5A";
    private const ulong HotfixAmount = 20_000_000_000;
    private static readonly ulong[] TestNetHotfixRounds = [1_499_995, 2_926_564];

    private static readonly byte[] TestNetGenesis = Convert.FromBase64String(TestNetGenesisHash);

    /// <summary>The transfer applied at the start of the round, or null when the round has none.</summary>
    public static (Address From, Address To, BigInteger Amount)? StartOfRound(ReadOnlySpan<byte> genesisHash, ulong round, Address rewardsPool)
    {
        if (!genesisHash.SequenceEqual(TestNetGenesis) || !TestNetHotfixRounds.Contains(round))
        {
            return null;
        }

        return (new Address(new Algorand.Address(TestNetBank).Bytes), rewardsPool, new BigInteger(HotfixAmount));
    }
}
