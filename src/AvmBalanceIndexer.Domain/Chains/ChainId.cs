namespace AvmBalanceIndexer.Domain.Chains;

/// <summary>Well-known chain identifiers used in the /v1/{chain} route and as the storage namespace.</summary>
public static class ChainId
{
    public const string AlgorandMainnet = "algorand-mainnet";
    public const string AlgorandTestnet = "algorand-testnet";
    public const string Voi = "voi";

    public static readonly IReadOnlyList<string> All = [AlgorandMainnet, AlgorandTestnet, Voi];

    public static bool IsKnown(string chain) => All.Contains(chain, StringComparer.Ordinal);
}
