namespace AvmBalanceIndexer.Domain.Effects;

/// <summary>Why an account/token balance changed. Every type maps to a deterministic ledger operation.</summary>
public enum EffectType
{
    Payment = 0,
    Fee = 1,
    Reward = 2,
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
}
