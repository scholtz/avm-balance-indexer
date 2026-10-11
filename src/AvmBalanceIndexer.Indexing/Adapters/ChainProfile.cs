using Algorand.Gossip;
using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Static facts about a supported chain: gossip network, genesis identity and native token naming.</summary>
/// <param name="Chain">Chain id used in routes and storage, for example <c>algorand-testnet</c>.</param>
/// <param name="Network">Network used for DNS SRV archival node discovery.</param>
/// <param name="GenesisId">Genesis id as it appears in block headers.</param>
/// <param name="GenesisHash">Expected base64 genesis hash, or null when it must be configured explicitly.</param>
/// <param name="DefaultAlgodUrl">Public algod endpoint used for status queries when none is configured.</param>
/// <param name="NativeSymbol">Native token symbol.</param>
/// <param name="NativeName">Native token name.</param>
public sealed record ChainProfile(
    string Chain,
    GossipNetwork Network,
    string GenesisId,
    string? GenesisHash,
    string DefaultAlgodUrl,
    string NativeSymbol,
    string NativeName)
{
    /// <summary>Returns the profile for a known chain id.</summary>
    /// <exception cref="ArgumentException">The chain id is not supported.</exception>
    public static ChainProfile For(string chain) => chain switch
    {
        ChainId.AlgorandMainnet => new ChainProfile(
            chain,
            GossipNetwork.AlgorandMainNet,
            GossipHttpConfiguration.GetGenesisId(GossipNetwork.AlgorandMainNet),
            "wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8=",
            "https://mainnet-api.4160.nodely.dev",
            "ALGO",
            "Algo"),
        ChainId.AlgorandTestnet => new ChainProfile(
            chain,
            GossipNetwork.AlgorandTestNet,
            GossipHttpConfiguration.GetGenesisId(GossipNetwork.AlgorandTestNet),
            "SGO1GKSzyE7IEPItTxCByw9x8FmnrCDexi9/cOUJOiI=",
            "https://testnet-api.4160.nodely.dev",
            "ALGO",
            "Algo"),
        ChainId.Voi => new ChainProfile(
            chain,
            GossipNetwork.VoiMainNet,
            GossipHttpConfiguration.GetGenesisId(GossipNetwork.VoiMainNet),
            "r20fSQI8gWe/kFZziNonSPCXLwcQmH/nxROvnnueWOk=",
            "https://mainnet-api.voi.nodely.dev",
            "VOI",
            "Voi"),
        _ => throw new ArgumentException($"Unsupported chain '{chain}'.", nameof(chain)),
    };
}
