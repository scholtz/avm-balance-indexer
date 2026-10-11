using Algorand.Algod.Model;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// A validated block as fetched from an archival node: the SDK model plus the ledger apply data the SDK does not expose.
/// </summary>
/// <param name="Round">Block round.</param>
/// <param name="Hash">Block hash taken from the certificate's proposal digest.</param>
/// <param name="PreviousHash">Hash of the previous block as recorded in this block's header.</param>
/// <param name="GenesisHash">Genesis hash from the header.</param>
/// <param name="GenesisId">Genesis id from the header.</param>
/// <param name="Block">Block header decoded by the Algorand5 SDK; its transaction list is empty, the transactions are in <paramref name="ApplyData"/>.</param>
/// <param name="ApplyData">The transactions in block order: raw transaction map, apply data (rewards, closing amounts, logs) and inner transactions.</param>
public sealed record BlockData(
    ulong Round,
    byte[] Hash,
    byte[] PreviousHash,
    byte[] GenesisHash,
    string GenesisId,
    Block Block,
    IReadOnlyList<TxnApplyData> ApplyData);
