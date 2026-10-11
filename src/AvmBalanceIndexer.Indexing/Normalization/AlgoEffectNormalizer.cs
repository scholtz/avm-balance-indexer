using System.Numerics;
using Algorand.Algod.Model.Transactions;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using DomainAddress = AvmBalanceIndexer.Domain.Chains.Address;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>
/// Derives the native token (ALGO/VOI) balance deltas of a block in ledger execution order.
/// <list type="number">
/// <item>Start of block: known manual ledger adjustments, then the rewards pool withdrawal derived from the header.</item>
/// <item>Per transaction: realized rewards, fee (debit and fee sink credit), payment amount, close-remainder-to,
/// then inner transactions depth first. This is the order the ledger applies them, so no intermediate balance is
/// negative when the block is valid.</item>
/// <item>End of block: the proposer payout, when present.</item>
/// </list>
/// Intra-round order is 0 for start-of-block effects, the transaction index plus one for transaction effects, and
/// the transaction count plus one for end-of-block effects.
/// <para>
/// Known limit: the ledger moves the proposer payout without recording rewards realized on the proposer in the block. An
/// online proposer that still has unrealized rewards from before the rewards rate dropped to zero is therefore credited
/// slightly more in the ledger than here, once, at its first payout. Tracking each account's rewards base would close it.
/// </para>
/// </summary>
public sealed class AlgoEffectNormalizer : IEffectNormalizer
{
    public IReadOnlyList<BalanceDelta> Normalize(BlockData block, RewardsState previousRewards) =>
        NormalizeBlock(block, previousRewards, AssetDirectory.Empty).Deltas;

    public NormalizedBlock NormalizeBlock(BlockData block, RewardsState previousRewards, IAssetDirectory assets)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(assets);

        var header = block.Block;
        var feeSink = header.FeeSink is { } sink
            ? ToDomain(sink, "fee sink")
            : throw new InvalidDataException($"Round {block.Round}: block header has no fee sink.");

        var transactions = (header.Transactions ?? []).ToArray();
        var round = new RoundBuilder(block.Round, feeSink, assets);

        round.BeginTransaction(0, string.Empty);
        ApplyStartOfBlock(round, block, previousRewards);

        for (var index = 0; index < transactions.Length; index++)
        {
            var apply = block.ApplyData[index];
            var transactionId = TransactionIdCalculator.Compute(apply, block.GenesisHash, block.GenesisId);
            round.BeginTransaction(index + 1, transactionId);
            ApplyTransaction(round, transactions[index], apply, []);
        }

        if (header.ProposerPayout is > 0)
        {
            var proposer = header.Proposer
                ?? throw new InvalidDataException($"Round {block.Round}: block header pays a proposer payout but names no proposer.");
            round.BeginTransaction(transactions.Length + 1, string.Empty);
            var payout = new BigInteger(header.ProposerPayout.Value);
            var proposerAddress = ToDomain(proposer, "proposer");
            round.Add(feeSink, -payout, EffectType.ProposerPayout, proposerAddress, []);
            round.Add(proposerAddress, payout, EffectType.ProposerPayout, feeSink, []);
        }

        return new NormalizedBlock(round.Deltas, round.TokenChanges);
    }

    private static void ApplyStartOfBlock(RoundBuilder round, BlockData block, RewardsState previousRewards)
    {
        var header = block.Block;
        var withdrawal = RewardsState.PoolWithdrawal(previousRewards, RewardsState.FromHeader(header), header.CurrentProtocol);
        if (header.RewardsPool is not { } poolAddress)
        {
            if (withdrawal.IsZero)
            {
                return;
            }

            throw new InvalidDataException($"Round {block.Round}: block header moves rewards but has no rewards pool.");
        }

        var pool = ToDomain(poolAddress, "rewards pool");
        if (KnownLedgerAdjustments.StartOfRound(block.GenesisHash, block.Round, pool) is { } adjustment)
        {
            round.Add(adjustment.From, -adjustment.Amount, EffectType.LedgerAdjustment, adjustment.To, []);
            round.Add(adjustment.To, adjustment.Amount, EffectType.LedgerAdjustment, adjustment.From, []);
        }

        round.Add(pool, -withdrawal, EffectType.RewardsWithdrawal, null, []);
    }

    private static void ApplyTransaction(RoundBuilder round, SignedTransaction signed, TxnApplyData apply, int[] path)
    {
        var transaction = signed.Tx ?? throw new InvalidDataException("Block transaction without a txn body.");
        var sender = ToDomain(transaction.Sender, "sender");
        var payment = transaction as PaymentTransaction;

        // The receiver field is omitted from the stored transaction when it is the zero address (a burn).
        var receiver = payment is null ? (DomainAddress?)null : payment.Receiver is null ? ZeroAddress : ToDomain(payment.Receiver, "receiver");
        var closeTo = payment?.CloseRemainderTo is { } close && !IsZero(close) ? ToDomain(close, "close-to") : (DomainAddress?)null;

        round.Add(sender, apply.SenderRewards, EffectType.Reward, null, path);
        if (receiver is { } rewardedReceiver)
        {
            round.Add(rewardedReceiver, apply.ReceiverRewards, EffectType.Reward, null, path);
        }

        if (closeTo is { } rewardedCloseTo)
        {
            round.Add(rewardedCloseTo, apply.CloseRewards, EffectType.Reward, null, path);
        }

        var fee = new BigInteger(transaction.Fee);
        round.Add(sender, -fee, EffectType.Fee, round.FeeSink, path);
        round.Add(round.FeeSink, fee, EffectType.FeeCollected, sender, path);

        if (payment is not null && receiver is { } to)
        {
            var amount = new BigInteger(payment.Amount ?? 0);
            round.Add(sender, -amount, EffectType.Payment, to, path);
            round.Add(to, amount, EffectType.Payment, sender, path);

            if (closeTo is { } destination)
            {
                var closing = new BigInteger(apply.ClosingAmount);
                round.Add(sender, -closing, EffectType.CloseOut, destination, path);
                round.Add(destination, closing, EffectType.CloseOut, sender, path);
            }
        }

        if (!apply.RawTxn.IsEmpty)
        {
            ApplyAsset(round, sender, apply, path);
        }

        var inner = (signed.Detail?.InnerTxns ?? []).ToArray();
        for (var i = 0; i < inner.Length; i++)
        {
            ApplyTransaction(round, inner[i], apply.InnerTxns[i], [.. path, i]);
        }
    }

    private static void ApplyAsset(RoundBuilder round, DomainAddress sender, TxnApplyData apply, int[] path)
    {
        var raw = RawTransaction.Read(apply.RawTxn);
        switch (raw.Type)
        {
            case "axfer":
                ApplyAssetTransfer(round, sender, raw, apply, path);
                break;
            case "acfg":
                ApplyAssetConfiguration(round, sender, raw, apply, path);
                break;
            case "appl":
                ApplyApplicationLogs(round, raw, apply, path);
                break;
        }
    }

    private static void ApplyApplicationLogs(RoundBuilder round, RawTransaction raw, TxnApplyData apply, int[] path)
    {
        if (apply.Logs is not { Count: > 0 } logs)
        {
            return;
        }

        // The application that ran is the one called, or the one this call created.
        var application = raw.ApplicationId != 0 ? raw.ApplicationId : apply.CreatedApplicationId;
        if (application == 0)
        {
            return;
        }

        var token = TokenKey.Arc200(application);
        foreach (var log in logs)
        {
            if (!Arc200Events.TryParseTransfer(log.Span, out var fromBytes, out var toBytes, out var amount) || amount.IsZero)
            {
                continue;
            }

            var from = new DomainAddress(fromBytes);
            var to = new DomainAddress(toBytes);
            var minted = from == ZeroAddress;
            var burned = to == ZeroAddress;
            if (minted && burned)
            {
                continue;
            }

            round.RecordArc200(application);
            if (!minted)
            {
                round.Add(from, -amount, token, burned ? EffectType.Arc200Burn : EffectType.Arc200Transfer, burned ? null : to, path);
            }

            if (!burned)
            {
                round.Add(to, amount, token, minted ? EffectType.Arc200Mint : EffectType.Arc200Transfer, minted ? null : from, path);
            }
        }
    }

    private static void ApplyAssetTransfer(RoundBuilder round, DomainAddress sender, RawTransaction raw, TxnApplyData apply, int[] path)
    {
        var token = TokenKey.Asa(raw.TransferAssetId);
        var source = raw.AssetSender is { } clawback ? new DomainAddress(clawback) : sender;
        var receiver = raw.AssetReceiver is { } to ? new DomainAddress(to) : ZeroAddress;
        var closeTo = raw.AssetCloseTo is { } close && !close.All(static b => b == 0) ? new DomainAddress(close) : (DomainAddress?)null;
        var amount = new BigInteger(raw.TransferAmount);

        // An asset transfer of zero units from an account to itself is how an account opts in.
        if (raw.AssetSender is null && amount.IsZero && receiver == sender && closeTo is null)
        {
            round.Add(sender, BigInteger.Zero, token, EffectType.AssetOptIn, null, path, keepZero: true);
            return;
        }

        if (!amount.IsZero)
        {
            var type = raw.AssetSender is null ? EffectType.AssetTransfer : EffectType.AssetClawback;
            round.Add(source, -amount, token, type, receiver, path);
            round.Add(receiver, amount, token, type, source, path);
        }

        if (closeTo is { } destination)
        {
            // Closing out sends the sender's remaining units to the destination and removes the holding.
            var closing = new BigInteger(apply.AssetClosingAmount);
            round.Add(sender, -closing, token, EffectType.AssetOptOut, destination, path, keepZero: true);
            round.Add(destination, closing, token, EffectType.AssetTransfer, sender, path);
        }
    }

    private static void ApplyAssetConfiguration(RoundBuilder round, DomainAddress sender, RawTransaction raw, TxnApplyData apply, int[] path)
    {
        if (raw.ConfigAssetId == 0)
        {
            // The ledger creates an asset whenever the config asset id is zero; apar is omitted when all its fields are zero.
            var parameters = raw.Params ?? new RawAssetParams(0, 0, null, null, null);
            var assetId = apply.CreatedAssetId != 0
                ? apply.CreatedAssetId
                : throw new InvalidDataException($"Round {round.Round}: an asset creation has no created asset id in its apply data.");
            round.Record(AssetRecords.Created(assetId, sender, parameters, round.Round));
            round.Add(sender, new BigInteger(parameters.Total), TokenKey.Asa(assetId), EffectType.AssetCreate, null, path, keepZero: true);
        }
        else if (raw.Params is null)
        {
            var info = round.FindAsset(raw.ConfigAssetId)
                ?? throw new InvalidDataException($"Round {round.Round}: asset {raw.ConfigAssetId} is destroyed but was never seen created.");
            var creator = info.Creator ?? throw new InvalidDataException($"Asset {raw.ConfigAssetId} has no creator.");
            var token = TokenKey.Asa(raw.ConfigAssetId);

            // The ledger only destroys an asset whose whole supply is held by the creator, and removes that holding.
            round.Add(creator, -new BigInteger(AssetRecords.Total(info)), token, EffectType.AssetDestroy, null, path, keepZero: true);
            round.Add(creator, BigInteger.Zero, token, EffectType.AssetOptOut, null, path, keepZero: true);
            round.Record(info with { DeletedRound = round.Round });
        }

        // Reconfiguring an existing asset (caid with apar) moves no units; its effect on the token record is #34.
    }

    private static readonly DomainAddress ZeroAddress = new(new byte[DomainAddress.Length]);

    private static DomainAddress ToDomain(Algorand.Address? address, string role) =>
        address is null ? throw new InvalidDataException($"A transaction has no {role} address.") : new DomainAddress(address.Bytes);

    private static bool IsZero(Algorand.Address address) => address.Bytes.All(static b => b == 0);

    private sealed class RoundBuilder(ulong round, DomainAddress feeSink, IAssetDirectory assets)
    {
        private readonly List<BalanceDelta> _deltas = [];
        private readonly List<TokenInfo> _tokenChanges = [];
        private readonly Dictionary<ulong, TokenInfo> _assets = [];
        private readonly HashSet<ulong> _arc200 = [];
        private int _transactionIndex;
        private string _transactionId = string.Empty;

        public ulong Round { get; } = round;

        public DomainAddress FeeSink { get; } = feeSink;

        public IReadOnlyList<BalanceDelta> Deltas => _deltas;

        public IReadOnlyList<TokenInfo> TokenChanges => _tokenChanges;

        public void BeginTransaction(int index, string transactionId)
        {
            _transactionIndex = index;
            _transactionId = transactionId;
        }

        public TokenInfo? FindAsset(ulong assetId) => _assets.TryGetValue(assetId, out var info) ? info : assets.Find(assetId);

        public void RecordArc200(ulong applicationId)
        {
            if (_arc200.Add(applicationId))
            {
                // Detection is by event shape only, so the standard is not verified.
                _tokenChanges.Add(new TokenInfo(TokenKey.Arc200(applicationId), null, null, null, null, TokenStandard.Arc200, false, null, Round, null, null));
            }
        }

        public void Record(TokenInfo info)
        {
            _assets[info.Key.TokenId] = info;
            _tokenChanges.Add(info);
        }

        public void Add(DomainAddress account, ulong amount, EffectType type, DomainAddress? counterparty, int[] path) =>
            Add(account, new BigInteger(amount), TokenKey.Native, type, counterparty, path);

        public void Add(DomainAddress account, BigInteger delta, EffectType type, DomainAddress? counterparty, int[] path) =>
            Add(account, delta, TokenKey.Native, type, counterparty, path);

        // keepZero records the effect although it changes no balance (opt-in, opt-out, creation).
        public void Add(DomainAddress account, BigInteger delta, TokenKey token, EffectType type, DomainAddress? counterparty, int[] path, bool keepZero = false)
        {
            if (delta.IsZero && !keepZero)
            {
                return;
            }

            var id = new EffectId(Round, _transactionIndex, _transactionId, path, _deltas.Count);
            _deltas.Add(new BalanceDelta(id, account, token, type, delta, counterparty));
        }
    }
}
