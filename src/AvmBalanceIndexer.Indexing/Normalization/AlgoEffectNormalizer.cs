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
/// </summary>
public sealed class AlgoEffectNormalizer : IEffectNormalizer
{
    public IReadOnlyList<BalanceDelta> Normalize(BlockData block, RewardsState previousRewards)
    {
        ArgumentNullException.ThrowIfNull(block);

        var header = block.Block;
        var feeSink = header.FeeSink is { } sink
            ? ToDomain(sink)
            : throw new InvalidDataException($"Round {block.Round}: block header has no fee sink.");

        var transactions = (header.Transactions ?? []).ToArray();
        var round = new RoundBuilder(block.Round, feeSink);

        round.BeginTransaction(0, string.Empty);
        ApplyStartOfBlock(round, block, previousRewards);

        for (var index = 0; index < transactions.Length; index++)
        {
            var apply = block.ApplyData[index];
            var transactionId = TransactionIdCalculator.Compute(apply, block.GenesisHash, block.GenesisId);
            round.BeginTransaction(index + 1, transactionId);
            ApplyTransaction(round, transactions[index], apply, []);
        }

        if (header.ProposerPayout is > 0 && header.Proposer is { } proposer)
        {
            round.BeginTransaction(transactions.Length + 1, string.Empty);
            var payout = new BigInteger(header.ProposerPayout.Value);
            var proposerAddress = ToDomain(proposer);
            round.Add(feeSink, -payout, EffectType.ProposerPayout, proposerAddress, []);
            round.Add(proposerAddress, payout, EffectType.ProposerPayout, feeSink, []);
        }

        return round.Deltas;
    }

    private static void ApplyStartOfBlock(RoundBuilder round, BlockData block, RewardsState previousRewards)
    {
        var header = block.Block;
        if (header.RewardsPool is not { } poolAddress)
        {
            return;
        }

        var pool = ToDomain(poolAddress);
        if (KnownLedgerAdjustments.StartOfRound(block.GenesisHash, block.Round, pool) is { } adjustment)
        {
            round.Add(adjustment.From, -adjustment.Amount, EffectType.LedgerAdjustment, adjustment.To, []);
            round.Add(adjustment.To, adjustment.Amount, EffectType.LedgerAdjustment, adjustment.From, []);
        }

        var withdrawal = RewardsState.PoolWithdrawal(previousRewards, RewardsState.FromHeader(header), header.CurrentProtocol);
        round.Add(pool, -withdrawal, EffectType.RewardsWithdrawal, null, []);
    }

    private static void ApplyTransaction(RoundBuilder round, SignedTransaction signed, TxnApplyData apply, int[] path)
    {
        var transaction = signed.Tx ?? throw new InvalidDataException("Block transaction without a txn body.");
        var sender = ToDomain(transaction.Sender);
        var payment = transaction as PaymentTransaction;
        var receiver = payment is null ? (DomainAddress?)null : ToDomain(payment.Receiver);
        var closeTo = payment?.CloseRemainderTo is { } close && !IsZero(close) ? ToDomain(close) : (DomainAddress?)null;

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

        var inner = (signed.Detail?.InnerTxns ?? []).ToArray();
        for (var i = 0; i < inner.Length; i++)
        {
            ApplyTransaction(round, inner[i], apply.InnerTxns[i], [.. path, i]);
        }
    }

    private static DomainAddress ToDomain(Algorand.Address address) =>
        new(address?.Bytes ?? new byte[DomainAddress.Length]);

    private static bool IsZero(Algorand.Address address) => address.Bytes.All(static b => b == 0);

    private sealed class RoundBuilder(ulong round, DomainAddress feeSink)
    {
        private readonly List<BalanceDelta> _deltas = [];
        private int _transactionIndex;
        private string _transactionId = string.Empty;

        public DomainAddress FeeSink { get; } = feeSink;

        public IReadOnlyList<BalanceDelta> Deltas => _deltas;

        public void BeginTransaction(int index, string transactionId)
        {
            _transactionIndex = index;
            _transactionId = transactionId;
        }

        public void Add(DomainAddress account, ulong amount, EffectType type, DomainAddress? counterparty, int[] path) =>
            Add(account, new BigInteger(amount), type, counterparty, path);

        public void Add(DomainAddress account, BigInteger delta, EffectType type, DomainAddress? counterparty, int[] path)
        {
            if (delta.IsZero)
            {
                return;
            }

            var id = new EffectId(round, _transactionIndex, _transactionId, path, _deltas.Count);
            _deltas.Add(new BalanceDelta(id, account, TokenKey.Native, type, delta, counterparty));
        }
    }
}
