namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Consensus protocol facts the block decoder needs, keyed by the protocol string of the block header. Only protocols v0 to
/// v25 differ in what is listed here; every later or unknown protocol inherits the newest behaviour. A network on a fork
/// with its own older protocol strings would need its strings added to <see cref="Legacy"/>.
/// </summary>
public static class ConsensusProtocols
{
    /// <summary>The protocols v0 to v25 in activation order, so the position is the version number.</summary>
    private static readonly string[] Legacy =
    [
        "v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9", "v10", "v11", "v12",
        "https://github.com/algorand/spec/tree/0c8a9dc44d7368cc266d5407b79fb3311f4fc795",
        "https://github.com/algorand/spec/tree/2526b6ae062b4fe5e163e06e41e1d9b9219135a9",
        "https://github.com/algorand/spec/tree/a26ed78ed8f834e2b9ccb6eb7d3ee9f629a6e622",
        "https://github.com/algorand/spec/tree/22726c9dcd12d9cddce4a8bd7e8ccaa707f74101",
        "https://github.com/algorandfoundation/specs/tree/5615adc36bad610c7f165fa2967f4ecfa75125f0",
        "https://github.com/algorandfoundation/specs/tree/6c6bd668be0ab14098e51b37e806c509f7b7e31f",
        "https://github.com/algorandfoundation/specs/tree/0e196e82bfd6e327994bec373c4cc81bc878ef5c",
        "https://github.com/algorandfoundation/specs/tree/4a9db6a25595c6fd097cf9cc137cc83027787eaa",
        "https://github.com/algorandfoundation/specs/tree/8096e2df2da75c3339986317f9abe69d4fa86b4b",
        "https://github.com/algorandfoundation/specs/tree/57016b942f6d97e6d4c0688b373bb0a2fc85a1a2",
        "https://github.com/algorandfoundation/specs/tree/e5f565421d720c6f75cdd186f7098495caf9101f",
        "https://github.com/algorandfoundation/specs/tree/3a83c4c743f8b17adfd73944b4319c25722a6782",
        "https://github.com/algorandfoundation/specs/tree/bea19289bf41217d2c0af30522fa222ef1366466",
    ];

    private const int FirstFlatCommitment = 11;
    private const int FirstRequiredGenesisHash = 16;
    private const int FirstRecordedAssetClosingAmount = 25;

    /// <summary>Protocols before v11 (blocks hold plain signed transactions) do not commit to the payset in the header.</summary>
    public static bool HasNoPaysetCommitment(string protocol) => Version(protocol) is { } v && v < FirstFlatCommitment;

    /// <summary>Protocols v11 to v25 commit to the payset with one flat hash; v26 and later use a Merkle tree.</summary>
    public static bool UsesFlatPaysetCommitment(string protocol) => Version(protocol) is { } v && v >= FirstFlatCommitment;

    /// <summary>
    /// From v16 on a block never flags the genesis hash: the ledger restores it on every stored transaction, so it is part of
    /// every transaction id. Before v16 it is restored only when the transaction is flagged.
    /// </summary>
    public static bool RequiresGenesisHash(string protocol) => Version(protocol) is not { } v || v >= FirstRequiredGenesisHash;

    /// <summary>
    /// From v25 (<c>EnableAssetCloseAmount</c> in go-algorand's consensus parameters) the apply data of an asset close-out
    /// states the closed amount (<c>aca</c>, absent when zero). Earlier protocols never record it.
    /// </summary>
    public static bool RecordsAssetClosingAmount(string protocol) => Version(protocol) is not { } v || v >= FirstRecordedAssetClosingAmount;

    private static int? Version(string protocol)
    {
        var index = Array.IndexOf(Legacy, protocol);
        return index < 0 ? null : index;
    }
}
