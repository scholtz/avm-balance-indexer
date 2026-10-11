namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>Why an account/token balance changed. Every type maps to a deterministic ledger operation.</summary>
public enum EffectType
{
    /// <summary>Payment amount: negative on the sender, positive on the receiver.</summary>
    Payment = 0,

    /// <summary>Transaction fee debited from the paying account.</summary>
    Fee = 1,

    /// <summary>Protocol rewards realized on the account by a transaction (sender, receiver or close-to).</summary>
    Reward = 2,

    /// <summary>Remaining balance moved by a close-remainder-to payment.</summary>
    CloseOut = 3,
    AssetTransfer = 4,
    AssetOptIn = 5,
    AssetOptOut = 6,
    AssetCreate = 7,
    AssetDestroy = 8,
    AssetClawback = 9,
    Arc200Transfer = 10,
    Arc200Mint = 11,
    Arc200Burn = 12,

    /// <summary>Transaction fee credited to the chain's fee sink.</summary>
    FeeCollected = 13,

    /// <summary>Block proposer payout moved from the fee sink to the proposer.</summary>
    ProposerPayout = 14,

    /// <summary>Initial balance from the genesis allocation (round 0).</summary>
    Genesis = 15,

    /// <summary>Rewards distributed to accounts, withdrawn from the rewards pool at the start of every round.</summary>
    RewardsWithdrawal = 16,

    /// <summary>A protocol-level balance movement that happens without a transaction (historical ledger hotfixes).</summary>
    LedgerAdjustment = 17,
}
