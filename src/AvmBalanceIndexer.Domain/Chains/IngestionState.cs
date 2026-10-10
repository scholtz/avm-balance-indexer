namespace AvmBalanceIndexer.Domain.Chains;

public enum IngestionState
{
    Bootstrapping = 0,
    Backfilling = 1,
    Following = 2,
    Replaying = 3,
    Failed = 4,
}
