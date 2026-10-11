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
/// <param name="Block">Block decoded by the Algorand5 SDK.</param>
/// <param name="ApplyData">Per-transaction apply data in block order (rewards, closing amounts, inner transactions).</param>
public sealed record BlockData(
    ulong Round,
    byte[] Hash,
    byte[] PreviousHash,
    byte[] GenesisHash,
    string GenesisId,
    Block Block,
    IReadOnlyList<TxnApplyData> ApplyData);
