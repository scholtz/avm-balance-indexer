namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Ledger apply data of one block transaction (or inner transaction) that the Algorand5 model does not retain.
/// </summary>
/// <param name="RawTxn">Raw canonical msgpack of the <c>txn</c> map as stored in the block.</param>
/// <param name="SenderRewards">Rewards realized on the sender (<c>rs</c>).</param>
/// <param name="ReceiverRewards">Rewards realized on the payment receiver (<c>rr</c>).</param>
/// <param name="CloseRewards">Rewards realized on the close-remainder-to account (<c>rc</c>).</param>
/// <param name="ClosingAmount">Balance moved by close-remainder-to (<c>ca</c>).</param>
/// <param name="AssetClosingAmount">Asset balance moved by an asset close-to (<c>aca</c>).</param>
/// <param name="CreatedAssetId">Id of the asset created by this asset configuration transaction (<c>caid</c>), else zero.</param>
/// <param name="HasGenesisHash">The genesis hash was stripped from the stored transaction (<c>hgh</c>).</param>
/// <param name="HasGenesisId">The genesis id was stripped from the stored transaction (<c>hgi</c>).</param>
/// <param name="InnerTxns">Apply data of inner transactions in execution order.</param>
/// <param name="CreatedApplicationId">Id of the application created by this application call (<c>apid</c> in the apply data), else zero.</param>
/// <param name="Logs">Log messages the transaction emitted (<c>dt.lg</c>), in order.</param>
public sealed record TxnApplyData(
    ReadOnlyMemory<byte> RawTxn,
    ulong SenderRewards,
    ulong ReceiverRewards,
    ulong CloseRewards,
    ulong ClosingAmount,
    ulong AssetClosingAmount,
    ulong CreatedAssetId,
    bool HasGenesisHash,
    bool HasGenesisId,
    IReadOnlyList<TxnApplyData> InnerTxns,
    ulong CreatedApplicationId = 0,
    IReadOnlyList<ReadOnlyMemory<byte>>? Logs = null);
